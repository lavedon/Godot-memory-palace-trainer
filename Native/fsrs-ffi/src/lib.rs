//! C ABI over the `fsrs` crate for the Palace Room Viewer. Only what the viewer needs:
//! replay review histories into memory states, and the recall probability after some days.
//! Every exported function catches panics so nothing unwinds into the .NET runtime.

use std::panic::catch_unwind;
use std::slice;

use fsrs::{DEFAULT_PARAMETERS, FSRS, FSRSItem, FSRSReview, MemoryState, current_retrievability};

pub const FSRS_FFI_OK: i32 = 0;
pub const FSRS_FFI_INVALID_INPUT: i32 = 1;
pub const FSRS_FFI_FAILED: i32 = 2;

/// Number of FSRS-6 parameters, including the decay at index 20.
pub const FSRS_FFI_PARAMETER_COUNT: usize = 21;

/// Bumped when an exported signature changes, so the viewer can refuse a stale DLL.
#[unsafe(no_mangle)]
pub extern "C" fn fsrs_ffi_abi_version() -> u32 {
    1
}

/// Copies the default FSRS-6 parameters into `out` (21 floats).
///
/// # Safety
/// `out` must point to at least 21 writable floats.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsrs_ffi_default_parameters(out: *mut f32) -> i32 {
    if out.is_null() {
        return FSRS_FFI_INVALID_INPUT;
    }
    // SAFETY: the caller promises 21 writable floats at `out`.
    unsafe { slice::from_raw_parts_mut(out, FSRS_FFI_PARAMETER_COUNT) }.copy_from_slice(&DEFAULT_PARAMETERS);
    FSRS_FFI_OK
}

/// Replays several items' review histories into memory states.
///
/// The reviews of all items are concatenated in `ratings` and `delta_days` (1 = Again … 4 = Easy;
/// days since the item's previous review, 0 for its first). `lengths[i]` is item i's review count
/// and must be at least 1. Results go to `out_stability[i]` and `out_difficulty[i]`.
/// `parameters` may be null for the defaults, otherwise it holds 21 floats.
///
/// # Safety
/// Every pointer must be valid for the lengths described above.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsrs_ffi_memory_states(
    parameters: *const f32,
    ratings: *const u32,
    delta_days: *const u32,
    lengths: *const u32,
    item_count: usize,
    out_stability: *mut f32,
    out_difficulty: *mut f32,
) -> i32 {
    if item_count == 0 {
        return FSRS_FFI_OK;
    }
    if ratings.is_null() || delta_days.is_null() || lengths.is_null() || out_stability.is_null() || out_difficulty.is_null() {
        return FSRS_FFI_INVALID_INPUT;
    }
    // SAFETY: the caller promises these lengths; nothing here outlives the call.
    let (parameters, lengths, out_stability, out_difficulty) = unsafe {
        (
            if parameters.is_null() { &DEFAULT_PARAMETERS[..] } else { slice::from_raw_parts(parameters, FSRS_FFI_PARAMETER_COUNT) },
            slice::from_raw_parts(lengths, item_count),
            slice::from_raw_parts_mut(out_stability, item_count),
            slice::from_raw_parts_mut(out_difficulty, item_count),
        )
    };
    if lengths.contains(&0) {
        return FSRS_FFI_INVALID_INPUT;
    }
    let total: usize = lengths.iter().map(|&n| n as usize).sum();
    // SAFETY: as above, the caller promises `total` reviews.
    let (ratings, delta_days) = unsafe { (slice::from_raw_parts(ratings, total), slice::from_raw_parts(delta_days, total)) };
    if ratings.iter().any(|r| !(1..=4).contains(r)) {
        return FSRS_FFI_INVALID_INPUT;
    }
    let result = catch_unwind(|| memory_states(parameters, ratings, delta_days, lengths));
    match result {
        Ok(Some(states)) => {
            for (i, state) in states.iter().enumerate() {
                out_stability[i] = state.stability;
                out_difficulty[i] = state.difficulty;
            }
            FSRS_FFI_OK
        }
        _ => FSRS_FFI_FAILED,
    }
}

fn memory_states(parameters: &[f32], ratings: &[u32], delta_days: &[u32], lengths: &[u32]) -> Option<Vec<MemoryState>> {
    let fsrs = FSRS::new(parameters).ok()?;
    let mut start = 0;
    let mut states = Vec::with_capacity(lengths.len());
    for &length in lengths {
        let end = start + length as usize;
        let reviews = (start..end)
            .map(|i| FSRSReview { rating: ratings[i], delta_t: if i == start { 0 } else { delta_days[i] } })
            .collect();
        states.push(fsrs.memory_state(FSRSItem { reviews }, None).ok()?);
        start = end;
    }
    Some(states)
}

/// Probability of recall `days_elapsed` days after the last review. `decay` is parameter 20.
/// Returns a negative number for invalid input.
#[unsafe(no_mangle)]
pub extern "C" fn fsrs_ffi_retrievability(stability: f32, days_elapsed: f32, decay: f32) -> f32 {
    if !(stability > 0.0) || !(days_elapsed >= 0.0) || !(decay > 0.0) {
        return -1.0;
    }
    catch_unwind(|| current_retrievability(MemoryState { stability, difficulty: 5.0 }, days_elapsed, decay)).unwrap_or(-1.0)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn states(ratings: &[u32], deltas: &[u32], lengths: &[u32]) -> (i32, Vec<f32>, Vec<f32>) {
        let mut s = vec![0.0; lengths.len()];
        let mut d = vec![0.0; lengths.len()];
        let code = unsafe {
            fsrs_ffi_memory_states(std::ptr::null(), ratings.as_ptr(), deltas.as_ptr(), lengths.as_ptr(), lengths.len(), s.as_mut_ptr(), d.as_mut_ptr())
        };
        (code, s, d)
    }

    #[test]
    fn success_grows_stability_and_failure_resets_it() {
        let (code, s, _) = states(&[3, 3, 3, 3, 1], &[0, 0, 3, 0, 10], &[1, 2, 2]);
        assert_eq!(code, FSRS_FFI_OK);
        assert!(s[1] > s[0], "a second success should be more stable: {s:?}");
        assert!(s[2] < s[1], "a lapse should lower stability: {s:?}");
    }

    #[test]
    fn recall_falls_with_time_and_is_ninety_percent_at_stability() {
        let decay = DEFAULT_PARAMETERS[20];
        assert!((fsrs_ffi_retrievability(10.0, 0.0, decay) - 1.0).abs() < 1e-6);
        assert!((fsrs_ffi_retrievability(10.0, 10.0, decay) - 0.9).abs() < 1e-4);
        assert!(fsrs_ffi_retrievability(10.0, 30.0, decay) < 0.9);
        assert!(fsrs_ffi_retrievability(0.0, 1.0, decay) < 0.0);
    }

    #[test]
    fn rejects_bad_input() {
        assert_eq!(states(&[5], &[0], &[1]).0, FSRS_FFI_INVALID_INPUT);
        assert_eq!(states(&[], &[], &[0]).0, FSRS_FFI_INVALID_INPUT);
    }
}
