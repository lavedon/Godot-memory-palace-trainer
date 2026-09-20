-- Schema reference for the migration. Prefer scripts/migrate-room-images.ps1:
-- it makes a SQLite backup, checks compatibility, and safely skips existing columns.
BEGIN IMMEDIATE;
ALTER TABLE Rooms ADD COLUMN LeftImagePath TEXT NULL;
ALTER TABLE Rooms ADD COLUMN RightImagePath TEXT NULL;
ALTER TABLE Rooms ADD COLUMN ForwardImagePath TEXT NULL;
ALTER TABLE Rooms ADD COLUMN BackImagePath TEXT NULL;
ALTER TABLE Rooms ADD COLUMN FloorImagePath TEXT NULL;
ALTER TABLE Rooms ADD COLUMN CeilingImagePath TEXT NULL;
COMMIT;
