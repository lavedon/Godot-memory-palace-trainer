using PalaceRoomViewer.Migrations;

try
{
    if (args.Length is < 2 or > 3 || args[0] != "--database" || (args.Length == 3 && args[2] != "--check"))
        throw new ArgumentException("Usage: --database <existing SQLite path> [--check]");
    var result = RoomImagesMigration.Run(args[1], args.Length == 3);
    Console.WriteLine($"Database: {result.DatabasePath}");
    Console.WriteLine(result.Applied ? "Migration applied." : result.Columns.Count == 0 ? "Already migrated; no changes needed." : "Check only; no changes made.");
    Console.WriteLine($"{(result.Applied ? "Added" : "Missing")} columns: {string.Join(", ", result.Columns)}");
    if (result.BackupPath is not null) Console.WriteLine($"Backup: {result.BackupPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Migration failed: {ex.Message}");
    return 1;
}
