"""Rebuild the requirements graph from the three original specification documents.

Semantic records were extracted by a delegated Codex agent. This is a requirements
map, not a claim that the planned application has passed acceptance.
"""
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
import json
import re
import sys

SITE = Path('C:/Users/Owner/AppData/Roaming/uv/tools/graphifyy/Lib/site-packages')
if SITE.exists():
    sys.path.insert(0, str(SITE))

from graphify.detect import detect, save_manifest
from graphify.cache import check_semantic_cache, save_semantic_cache
from graphify.build import build_from_json
from graphify.cluster import cluster, score_all
from graphify.analyze import god_nodes, surprising_connections, suggest_questions
from graphify.report import generate
from graphify.export import to_json, to_html

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'graphify-out'
FILES = ['CONTEXT.md', 'room-loci.md', 'docs/adr/0001-csharp-godot-reads-palace-db-directly.md']
TEXT = {f: (ROOT / f).read_text(encoding='utf-8') for f in FILES}
NODES, EDGES, HYPEREDGES = [], [], []
IDS = {}

def write(name, data):
    (OUT / name).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')

def location(file, quote):
    offset = TEXT[file].find(quote)
    assert offset >= 0, (file, quote)
    return f'{file}:{TEXT[file][:offset].count(chr(10)) + 1}'

def node(file_index, entity, label, quote, description='', group='', rationale=None):
    file = FILES[file_index]
    stem = Path(file).stem if '/' not in file else Path(file).parent.name + '_' + Path(file).stem
    nid = re.sub('[^a-z0-9_]', '_', stem.lower()) + '_' + entity
    record = dict(id=nid, label=label, file_type='rationale', source_file=file,
                  source_location=location(file, quote), description=description,
                  source_url=None, captured_at=None, author=None, contributor=None,
                  requirement_group=group)
    if rationale:
        record['rationale'] = rationale
    NODES.append(record)
    IDS[entity] = nid
    return nid

def edge(source, target, relation, file_index, quote, confidence='EXTRACTED', score=1.0):
    file = FILES[file_index]
    EDGES.append(dict(source=IDS[source], target=IDS[target], relation=relation,
                      confidence=confidence, confidence_score=score, weight=1.0,
                      source_file=file, source_location=location(file, quote)))

# The glossary defines the stable domain and records resolution of early wording.
node(0, 'palace', 'Palace', '**Palace**:', 'Ordered collection of Rooms; each Room belongs to exactly one Palace.', 'Domain language')
node(0, 'room', 'Room', '**Room**:', 'Render one Room at a time as a rectangular box with depth greater than width.', 'Domain language')
node(0, 'locus', 'Locus', '**Locus**', 'A memorized item whose full Text appears at one Position; higher Positions may exist in the shared database.', 'Domain language')
node(0, 'position', 'Stable Position identity', '**Position**:', 'Integer address 1–26; gaps preserve spatial identity and may not shift later Loci.', 'Position mapping')
node(0, 'slice', 'Horizontal Slices', '**Slice**:', 'Three horizontal bands: floor junction, center height, ceiling junction.', 'Position mapping')
node(0, 'wall_slot', 'Eight wall-slots', '**Wall-slot**:', 'Four corners and four wall centers per Slice; Position=(wall-slot−1)×3+Slice.', 'Position mapping')
node(0, 'anchor', 'Fixed Anchors and faint markers', '**Anchor / Marker**:', 'Every supported Position retains an Anchor and a faint numbered marker, even without a Locus.', 'Position mapping')
node(0, 'billboard', 'Billboard', '**Billboard**:', 'Camera-facing overlay showing Locus Text, toggled by J.', 'Billboard readability')
node(0, 'peg', 'Peg', '**Peg**:', 'Reusable mnemonic image linked to Loci; not rendered in this demo.', 'Deferred scope')
node(0, 'precedence', 'Review addenda take precedence', 'These clarifications take precedence', '2026-09-19 addenda override original cube, vertical-slice, copying, capacity, native-library and RoomImage wording.', 'Document precedence')
node(0, 'viewer_capacity', 'Viewer capacity is 26 Positions', '**Viewer capacity**:', 'The database may hold more Loci; place 1–26, warn on skipped higher Positions, never renumber or clamp.', 'Position validation')
node(0, 'orientation', 'World-fixed Room orientation', '**Room orientation**:', 'Front, back, left and right never mirror or move when the camera turns.', 'Position mapping')
node(0, 'position22', 'Position 22 correction resolved', '**Position 22 correction**:', 'The current first Slice diagram already has 22 in its bottom-left corner.', 'Document precedence')

# Plan requirements are intentionally individual records so acceptance can be traced.
node(1, 'demo', 'Single Room Windows demo', 'Keep one\nwalkable Room', 'A walkable rectangular memory-palace Room with 26 Anchors, WASD, mouse look and J text toggle.', 'Application scope')
node(1, 'plan_addendum', 'Demo requirements addendum', 'This addendum takes precedence', 'Implementation and acceptance requirements supersede conflicting original plan wording.', 'Document precedence')
node(1, 'historical_wording', 'Superseded early wording', 'The demo is like a FPS game', 'Cube, vertical slices and copy/import are historical wording only.', 'Document precedence')
node(1, 'room_arg', 'Required --room argument', 'Require `--room <id>`', 'Select an existing Room through required --room <id>; reject missing or invalid IDs visibly.', 'Read-only loading')
node(1, 'db_arg', 'Optional --db override', 'Support an optional `--db <path>`', 'Default C:\\tools\\Data\\palace.db, with optional arbitrary database path override.', 'Read-only loading')
node(1, 'title', 'Selected Room title', "Display the selected Room's title", 'Display the selected Room title in the viewer.', 'Read-only loading')
node(1, 'empty_room', 'Empty and missing Rooms differ', 'Distinguish an existing Room with no Loci', 'An existing empty Room displays all 26 empty markers; an unknown Room ID is a visible error.', 'Position validation')
node(1, 'valid_positions', 'Supported unique integer Positions', 'Place each Locus whose Position is an integer', 'Place only integer Positions 1–26; out-of-domain or malformed Positions must be reported.', 'Position validation')
node(1, 'duplicates', 'Lowest Id wins duplicates', 'on a duplicate Position, place the lowest Locus Id', 'Place the lowest Locus Id and visibly name every other skipped Locus at the duplicate Position.', 'Position validation')
node(1, 'warnings', 'Visible skipped-Loci warnings', 'emit a visible warning naming the skipped Loci', 'Warn for overflow, null and duplicate Positions while continuing to render valid Loci. Never silently discard, clamp or renumber.', 'Position validation')
node(1, 'gaps', 'Preserve missing Positions', 'Preserve gaps:', 'Map by Position value, never query result ordinal; Anchor 9 may remain empty while Locus 10 stays at Anchor 10.', 'Position validation')
node(1, 'errors', 'Visible loading errors', 'Report missing arguments, invalid or unknown Room IDs', 'Useful errors for missing arguments, invalid or unknown IDs, missing or unreadable files, incompatible schemas and database failures.', 'Read-only loading')
node(1, 'database_unchanged', 'Shared database unchanged', 'Keep the shared database unchanged', 'No repair, migration, schema creation, editing, or writes to test acceptance cases.', 'Read-only loading')
node(1, 'coordinate_convention', 'Record coordinate convention first', 'Before writing layout code, record a single coordinate convention', 'Record diagram front edge and viewing direction, world axes, dimensions, camera start position and facing before implementation.', 'Position mapping')
node(1, 'rectangle', 'Depth exceeds width', 'Depth must exceed width.', 'Rectangular Room with depth greater than width; exact dimensions are an implementation decision.', 'Position mapping')
node(1, 'front_cue', 'Obvious front-wall cue', 'an obvious visual cue for the front wall', 'Make the fixed front wall visually identifiable.', 'Position mapping')
node(1, 'floor_ceiling', 'Centered floor and ceiling Anchors', 'Positions 25 and 26 should use the centers', 'Position 25 is floor center; Position 26 is ceiling center.', 'Position mapping')
node(1, 'wall_mapping', 'Exact wall Position diagrams', 'Position = (wall-slot - 1) * 3 + Slice', 'Diagram bottom edge: 22/23/24 left corner, 1/2/3 center, 4/5/6 right corner. Right-center 7/8/9; top-right 10/11/12; top-center 13/14/15; top-left 16/17/18; left-center 19/20/21.', 'Position mapping')
node(1, 'walking', 'Fixed-height walking with wall collision', 'walking at a fixed eye height', 'WASD moves relative to horizontal camera facing; no flight or jumping; camera pitch never changes eye height and movement stays inside walls.', 'First-person controls')
node(1, 'mouse', 'Mouse capture and release', 'Capture the mouse for looking', 'Mouse controls look, Escape releases capture, and a viewer click recaptures.', 'First-person controls')
node(1, 'control_tuning', 'Tune speed and look limits', 'Define\nand tune movement speed', 'Define and visually tune movement speed, mouse sensitivity and vertical look limits.', 'First-person controls')
node(1, 'label3d', 'Label3D initial candidate', "Use Godot's `Label3D`", 'Initial camera-facing text implementation uses Godot Label3D billboarding and wrapping.', 'Billboard readability')
node(1, 'j_toggle', 'J toggles populated text', 'Start with Locus text hidden.', 'Initially hide all populated Billboards; each press of J toggles text, independently of markers.', 'Billboard readability')
node(1, 'k_toggle', 'K toggles numbered markers', 'The `k` key toggles the markers', 'Show faint numbered markers for all 26 Positions initially; K toggles markers independently of J.', 'Billboard readability')
node(1, 'text_offset', 'Inward text offsets', 'offset the displayed text inward', 'Keep logical Anchors fixed while offsetting rendered text to avoid intersections with walls, corners, floor and ceiling as it billboards.', 'Billboard readability')
node(1, 'full_text', 'Full wrapped readable text', 'Preserve\n  the full Locus text', 'Choose wrap width, font size, contrast and distance scaling together. Preserve text without silent truncation or assuming a permanent maximum length.', 'Billboard readability')
node(1, 'visual_checks', 'All 26 Billboard visual checks', 'Verify floor and ceiling text while looking up and down', 'Inspect floor/ceiling while pitching, corners from multiple walking positions, longest current text, longer wrapping sample, and overlap/clipping with all 26 enabled.', 'Acceptance checks')
node(1, 'scope_deferred', 'Deferred features', 'Defer `RoomImage` display', 'No RoomImage, Peg rendering, database editing, learning-session recording or inter-Room navigation in the first demo.', 'Deferred scope')
node(1, 'schema_reference', 'Schema is reference transcription', 'The SQL schema above is a reference transcription', 'Pasted schema contains wrapped comments that break executable SQL; restore comments before making an isolated fixture and never execute it against shared data.', 'Acceptance checks')
node(1, 'snapshot', 'Dated database observations', 'A read-only inspection', '2026-09-19 snapshot: 20 Rooms, 369 Loci; Room 7 had 1–29; Room 8 had 1–26; text lengths 4–117; no null, duplicate or gap Positions found. Recheck demo data.', 'Acceptance checks')
node(1, 'build_order', 'Three-stage build order', 'Build a walkable rectangular room', 'First layout/markers/controls; then Billboards and wrapping samples; finally validated read-only database integration and Windows export.', 'Acceptance checks')
node(1, 'full_acceptance', 'Full Room acceptance', 'A valid full Room places every Locus', 'Every Locus appears at its numbered Anchor and the Room title displays.', 'Acceptance checks')
node(1, 'partial_acceptance', 'Partial, gapped and empty acceptance', 'A partially populated Room and a fixture with gaps', 'Partial Rooms and gap fixtures preserve identities; empty Room retains all 26 markers.', 'Acceptance checks')
node(1, 'overflow_acceptance', 'Overflow, null and duplicate acceptance', 'Room 7 renders Positions 1–26', 'Room 7 warns visibly with skipped 27–29; isolated null and duplicate fixtures warn while rendering valid Loci.', 'Acceptance checks')
node(1, 'errors_acceptance', 'Failure paths never create or mutate data', 'Missing or invalid inputs and database failures', 'Useful loading errors without database creation or existing record changes.', 'Acceptance checks')
node(1, 'controls_acceptance', 'Movement and text-toggle acceptance', 'Walking stays inside the Room', 'Verify walls, fixed eye height, mouse release/recapture, all 26 readable from suitable positions, and repeated J affects text only.', 'Acceptance checks')
node(1, 'export_acceptance', 'Standalone Windows export acceptance', 'The Windows export loads SQLite', 'Test exported application as well as editor: packaged SQLite, --db override, no dependency on editor or C:\\tools\\e_sqlite3.dll.', 'Windows deployment')
node(1, 'fixtures', 'Isolated validation fixtures', 'Use isolated fixtures for invalid or synthetic data', 'Exercise invalid and synthetic cases in separate test databases; never modify shared palace.db.', 'Acceptance checks')

# ADR choices and deployment contract.
node(2, 'architecture', 'Godot .NET with direct SQLite', 'The viewer is built with the Godot 4.x', 'Godot 4 .NET/C# reads palace.db directly at load time through Microsoft.Data.Sqlite.', 'Windows deployment', 'Reuse the existing .NET tooling ecosystem without an engine-specific SQLite addon or drift-prone JSON export.')
node(2, 'adr_addendum', 'Database ADR addendum', 'This addendum supersedes the original', 'Direct SQLite and C# remain; previous claims of an existing override, shared native DLL dependence and RoomImage loading are superseded.', 'Document precedence')
node(2, 'read_only', 'Explicit Mode=ReadOnly', 'Open the database explicitly with `Mode=ReadOnly`', 'Prefer SqliteConnectionStringBuilder, with explicit ReadOnly so an invalid path cannot create an empty database.', 'Read-only loading')
node(2, 'parameterization', 'Parameterized Room query', 'Parameterize the Room ID', 'Parameterize Room Id and load selected Room title and Loci.', 'Read-only loading')
node(2, 'snapshot_load', 'Promptly dispose snapshot connection', 'promptly dispose\n  of the database connection', 'Load once and promptly dispose database connection; no continuous refresh or per-frame queries.', 'Read-only loading')
node(2, 'nuget_native', 'Package-managed native SQLite', "Use the project's `Microsoft.Data.Sqlite` NuGet dependency", 'Use Microsoft.Data.Sqlite with SQLitePCLRaw.bundle_e_sqlite3 and its packaged native runtime assets.', 'Windows deployment')
node(2, 'toolchain', 'Pin exact Windows toolchain', 'select and record exact versions', 'Record exact Godot .NET editor, matching export templates, .NET SDK, Microsoft.Data.Sqlite version and initial Windows architecture before implementation.', 'Windows deployment')
node(2, 'godot_dotnet', 'Godot .NET and SDK prerequisites', 'Development requires\nthe .NET SDK', 'Development needs the .NET edition of Godot and .NET SDK.', 'Windows deployment')
node(2, 'external_path', 'Test alternate database path', 'Test a database path outside the\ndefault location', 'Windows export must load an external database path with its own SQLite native dependency and no editor/shared-DLL requirement.', 'Windows deployment')
node(2, 'rejected', 'Rejected integration alternatives', 'The two\nalternatives were rejected', 'GDScript + godot-sqlite addon and offline JSON export were rejected.', 'Document precedence', 'GDExtension adds a vendored engine-specific binary; offline JSON adds a build stage and second data copy.')

# Explicit relationships, all traceable to a source statement.
for a,b,rel,fi,q in [
    ('palace','room','contains',0,'An ordered collection of Rooms'),
    ('room','locus','contains',0,'holding up to 26 Loci'),
    ('locus','position','placed_at',0,'placed at one Position'),
    ('wall_slot','slice','forms',0,'8 placements around a Slice'),
    ('wall_slot','position','determines',0,'Position = (wall-slot'),
    ('anchor','position','marks',0,'fixed in-world spot for one Position'),
    ('billboard','locus','displays',0,"showing a Locus's `Text`"),
    ('peg','locus','linked_to',0,'linked to Loci'),
    ('room','viewer_capacity','constrained_in_viewer_by',0,'**Viewer capacity**:'),
    ('position','gaps','preserved_by',0,'Missing Positions remain'),
    ('orientation','anchor','fixes',0,'must not mirror, reorder, or relocate its Anchors'),
    ('precedence','plan_addendum','references',0,'Implementation and acceptance details belong'),
    ('precedence','adr_addendum','references',0,'[database ADR]'),
    ('plan_addendum','historical_wording','supersedes',1,'This addendum takes precedence'),
    ('plan_addendum','position22','confirms',1,'Position 22 is already correct'),
    ('adr_addendum','architecture','confirms',2,'The C# and direct-database decisions stand'),
    ('architecture','rejected','rejects_alternatives',2,'alternatives were rejected'),
    ('valid_positions','warnings','reports_failures_with',1,'emit a visible warning naming'),
    ('duplicates','warnings','reports_losers_with',1,'place the lowest Locus Id and warn'),
    ('gaps','anchor','preserves',1,'a Locus at Position 10 stays at Anchor 10'),
    ('empty_room','anchor','retains',1,'an empty Room shows all 26 empty markers'),
    ('coordinate_convention','orientation','defines',1,'which diagram edge\nis the front wall'),
    ('coordinate_convention','rectangle','records',1,'room\ndimensions'),
    ('front_cue','orientation','identifies',1,'an obvious visual cue for the front wall'),
    ('floor_ceiling','anchor','locates',1,'Positions 25 and 26 should use the centers'),
    ('wall_mapping','position','locates',1,'The three diagrams cover Positions 1–24'),
    ('walking','mouse','paired_with',1,'Capture the mouse for looking'),
    ('control_tuning','walking','tunes',1,'movement speed'),
    ('control_tuning','mouse','tunes',1,'mouse sensitivity'),
    ('label3d','billboard','implements',1,'it provides billboarding and text wrapping'),
    ('j_toggle','billboard','toggles',1,'Each press of `j` toggles all populated Billboards'),
    ('k_toggle','anchor','toggles_markers',1,'The `k` key toggles the markers'),
    ('k_toggle','j_toggle','independent_of',1,'independently of the `j` text toggle'),
    ('text_offset','anchor','preserves',1,'Keep logical Anchors fixed'),
    ('text_offset','billboard','prevents_intersections',1,'as it faces the camera'),
    ('full_text','billboard','requires_readability',1,'do not silently truncate it to fit a Billboard'),
    ('visual_checks','full_text','verifies',1,'a longer wrapping sample'),
    ('visual_checks','floor_ceiling','verifies',1,'Verify floor and ceiling text'),
    ('scope_deferred','peg','defers',1,'Peg rendering'),
    ('schema_reference','fixtures','requires_comment_correction',1,'before reusing it in an executable fixture'),
    ('snapshot','overflow_acceptance','provides_demo_case',1,'Room 7 has 29 Loci'),
    ('full_acceptance','title','verifies',1,'displays its title'),
    ('full_acceptance','wall_mapping','verifies',1,'places every Locus at its numbered Anchor'),
    ('partial_acceptance','gaps','verifies',1,'a fixture with gaps preserve all Position identities'),
    ('partial_acceptance','empty_room','verifies',1,'an empty Room shows all 26 markers'),
    ('overflow_acceptance','warnings','verifies',1,'logs a visible warning listing the skipped'),
    ('overflow_acceptance','duplicates','verifies',1,'Fixtures also cover null and duplicate Positions'),
    ('errors_acceptance','errors','verifies',1,'produce useful errors'),
    ('errors_acceptance','database_unchanged','verifies',1,'without\n  creating a database or changing existing records'),
    ('controls_acceptance','walking','verifies',1,'Walking stays inside the Room'),
    ('controls_acceptance','mouse','verifies',1,'mouse\n  capture/release works'),
    ('controls_acceptance','j_toggle','verifies',1,'repeated `j` presses toggle only text'),
    ('controls_acceptance','visual_checks','verifies',1,'All 26 Billboards are readable'),
    ('fixtures','database_unchanged','protects',1,'do not change the shared database'),
    ('read_only','database_unchanged','enforces',2,'Never create, migrate, or repair'),
    ('architecture','read_only','requires',2,'Open the database explicitly'),
    ('parameterization','room_arg','uses',2,'Require a `--room <id>`'),
    ('snapshot_load','parameterization','disposes_after',2,'Parameterize the Room ID, load the Room'),
    ('architecture','nuget_native','depends_on',2,'Use the project\'s `Microsoft.Data.Sqlite` NuGet dependency'),
    ('architecture','godot_dotnet','requires',2,'the .NET SDK as well as'),
    ('toolchain','godot_dotnet','pins',2,'exact versions of the Godot .NET editor'),
    ('toolchain','nuget_native','pins',2,'`Microsoft.Data.Sqlite` package'),
    ('export_acceptance','nuget_native','verifies',1,'SQLite through its packaged dependencies'),
    ('export_acceptance','db_arg','verifies',1,'accepts a database\n  path override'),
    ('external_path','export_acceptance','verifies',2,'Verify the exported Windows application'),
    ('external_path','db_arg','exercises',2,'Test a database path outside'),
]:
    edge(a,b,rel,fi,q)

# Each source explicitly groups its implementation rules under these headings.
for n in list(NODES):
    if n['source_file'] == FILES[1] and n['requirement_group'] not in {'Document precedence', 'Application scope'}:
        entity = next(k for k,v in IDS.items() if v == n['id'])
        edge('demo',entity,'requires',1,'Agreed evaluation and demo requirements')

HYPEREDGES.append(dict(id='room_loading_contract',label='Read-only Room loading contract',
    nodes=[IDS[k] for k in ('room_arg','db_arg','read_only','parameterization','snapshot_load','warnings','errors')],
    relation='form', confidence='EXTRACTED',confidence_score=1.0, source_file=FILES[2]))
HYPEREDGES.append(dict(id='acceptance_suite',label='Demo acceptance suite',
    nodes=[IDS[k] for k in ('full_acceptance','partial_acceptance','overflow_acceptance','errors_acceptance','controls_acceptance','export_acceptance')],
    relation='form', confidence='EXTRACTED',confidence_score=1.0, source_file=FILES[1]))

def main():
    OUT.mkdir(exist_ok=True)
    (OUT / '.graphify_python').write_text(sys.executable, encoding='utf-8')
    (OUT / '.graphify_root').write_text(str(ROOT), encoding='utf-8')
    (OUT / '.graphify_sitepackages').write_text(str(SITE), encoding='utf-8')
    detected = detect(ROOT, extra_excludes=['*', '!CONTEXT.md', '!room-loci.md', '!docs/', '!docs/adr/', '!docs/adr/0001-csharp-godot-reads-palace-db-directly.md'])
    expected = {str((ROOT / f).resolve()) for f in FILES}
    detected['files'] = {kind:[f for f in values if f in expected] for kind,values in detected['files'].items()}
    assert sum(len(v) for v in detected['files'].values()) == 3, detected['files']
    detected['total_files'] = 3
    detected['total_words'] = sum(len(t.split()) for t in TEXT.values())
    detected['scope'] = 'Only the three original specification documents; application source excluded.'
    detected['warning'] = f"Corpus is ~{detected['total_words']:,} words and fits in one context window; this focused requirements graph supports implementation traceability."
    write('.graphify_detect.json', detected)
    cached_nodes, cached_edges, cached_hyperedges, uncached = check_semantic_cache(list(expected), root=ROOT)
    print(f"Corpus: 3 documents, ~{detected['total_words']} words; cache: {3-len(uncached)} hits. Delegated semantic extraction: 1 agent.")
    extraction = dict(nodes=NODES,edges=EDGES,hyperedges=HYPEREDGES,input_tokens=None,output_tokens=None,
                      token_usage='unavailable: host delegation API does not expose token counts')
    assert all(e['source'] in {n['id'] for n in NODES} and e['target'] in {n['id'] for n in NODES} for e in EDGES)
    write('requirements-extraction.json', extraction)
    save_semantic_cache(NODES, EDGES, HYPEREDGES, root=ROOT)
    graph = build_from_json(extraction)
    communities = cluster(graph)
    scores = score_all(graph, communities)
    node_lookup = {n['id']:n for n in NODES}
    community_names = {
        'demo': 'Room layout and validation',
        'architecture': 'Architecture and Windows deployment',
        'anchor': 'Stable Position identities',
        'walking': 'Walking and visual acceptance',
        'read_only': 'Safe loading and fixtures',
        'palace': 'Domain and deferred scope',
        'billboard': 'Independent text and markers',
        'snapshot_load': 'Room snapshot queries',
    }
    labels = {cid:next((label for entity,label in community_names.items() if IDS[entity] in members),
                       Counter(node_lookup[n]['requirement_group'] for n in members).most_common(1)[0][0])
              for cid,members in communities.items()}
    gods = god_nodes(graph)
    surprises = surprising_connections(graph, communities)
    questions = suggest_questions(graph, communities, labels)
    # The library formats numeric token fields. Replace its placeholder line before
    # writing any report because the host provides no real token measurements.
    report = generate(graph, communities, scores, labels, gods, surprises, detected,
                      {'input':0,'output':0},str(ROOT),suggested_questions=questions)
    report = re.sub(r'- Token cost:.*', '- Token cost: unavailable; the host delegation API did not report input or output counts.', report)
    # Obsidian export was not requested; link to actual sections in this report.
    hub_links = '\n'.join(f'- [Community {cid}: {label}](#community-{cid}---{label.lower().replace(" ", "-")})' for cid,label in labels.items())
    report = re.sub(r'(?<=## Community Hubs \(Navigation\)\n).*?(?=\n\n## God Nodes)',hub_links,report,flags=re.S)
    report += '\n\n## Scope and precedence\n\nThis graph captures requirements from the three original Markdown specifications only. It does not certify implementation or test completion. All extracted relationships have source locations. The 2026-09-19 addenda supersede conflicting original wording. Exact dimensions, camera pose, versions, movement and text settings are intentionally left as implementation decisions.\n'
    report += '\n## Acceptance checklist\n\n'
    for entity in ['coordinate_convention','toolchain','full_acceptance','partial_acceptance','overflow_acceptance','errors_acceptance','controls_acceptance','visual_checks','export_acceptance','fixtures']:
        n = node_lookup[IDS[entity]]
        report += f"- [ ] **{n['label']}**: {n['description']} Source: `{n['source_location']}`.\n"
    (OUT / 'GRAPH_REPORT.md').write_text(report,encoding='utf-8')
    to_json(graph, communities, str(OUT/'graph.json'))
    to_html(graph, communities, str(OUT/'graph.html'),community_labels=labels)
    write('.graphify_labels.json',{str(k):v for k,v in labels.items()})
    write('analysis.json',dict(communities=communities,cohesion=scores,labels=labels,gods=gods,surprises=surprises,questions=questions))
    save_manifest(detected['files'],manifest_path=str(OUT/'manifest.json'),root=ROOT)
    previous_runs = json.loads((OUT/'cost.json').read_text(encoding='utf-8')).get('runs',[]) if (OUT/'cost.json').exists() else []
    write('cost.json',dict(runs=previous_runs+[dict(date=datetime.now(timezone.utc).isoformat(),files=3,input_tokens=None,output_tokens=None)],
                          total_input_tokens=None,total_output_tokens=None,status='unavailable; host delegation API exposes no usage counts'))
    (OUT / '.graphify_detect.json').unlink(missing_ok=True)
    print(f'Graph complete: {graph.number_of_nodes()} nodes, {graph.number_of_edges()} edges, {len(communities)} communities. Token usage unavailable.')
    print('Community labels: '+', '.join(labels.values()))

if __name__ == '__main__':
    main()
