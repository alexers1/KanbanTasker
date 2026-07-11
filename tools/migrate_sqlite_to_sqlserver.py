"""One-time data migration: old SQLite ktdatabase.db -> SQL Server Express KanbanTasker.

Run with a Python that has pyodbc, e.g.:
  python migrate_sqlite_to_sqlserver.py [path-to-ktdatabase.db] --user kanban --password <pw>
  python migrate_sqlite_to_sqlserver.py --windows-auth

Boards/columns/tasks are appended with fresh identities (no ID collisions with
existing SQL Server data); relationships are remapped. Safe to re-run only if
you want duplicates - it does not dedupe.
"""
import argparse
import os
import sqlite3
import sys

import pyodbc

SQLITE_DEFAULT = os.path.join(os.environ["LOCALAPPDATA"], "Kanban.WinUI", "ktdatabase.db")


def build_connection_string(args) -> str:
    base = (
        f"DRIVER={{ODBC Driver 18 for SQL Server}};SERVER={args.server};DATABASE={args.database};"
        "TrustServerCertificate=yes;Encrypt=no;"
    )
    if args.windows_auth:
        return base + "Trusted_Connection=yes"
    if not args.user or not args.password:
        sys.exit("Provide --user and --password, or use --windows-auth.")
    return base + f"UID={args.user};PWD={args.password}"


def main() -> None:
    parser = argparse.ArgumentParser(description="Migrate KanbanTasker data from SQLite to SQL Server.")
    parser.add_argument("sqlite_path", nargs="?", default=SQLITE_DEFAULT)
    parser.add_argument("--server", default=r".\SQLEXPRESS")
    parser.add_argument("--database", default="KanbanTasker")
    parser.add_argument("--user", default="")
    parser.add_argument("--password", default="")
    parser.add_argument("--windows-auth", action="store_true")
    args = parser.parse_args()

    sqlite_path = args.sqlite_path
    if not os.path.exists(sqlite_path):
        sys.exit(f"SQLite database not found: {sqlite_path}")

    src = sqlite3.connect(sqlite_path)
    src.row_factory = sqlite3.Row
    dst = pyodbc.connect(build_connection_string(args))
    cur = dst.cursor()

    boards = src.execute("SELECT * FROM tblBoards").fetchall()
    columns = src.execute("SELECT * FROM tblColumns").fetchall()
    tasks = src.execute("SELECT * FROM tblTasks").fetchall()
    print(f"Source: {len(boards)} boards, {len(columns)} columns, {len(tasks)} tasks")

    board_id_map = {}
    for b in boards:
        cur.execute(
            "INSERT INTO tblBoards (Name, Notes) OUTPUT INSERTED.Id VALUES (?, ?)",
            b["Name"] or "", b["Notes"] or "",
        )
        board_id_map[b["Id"]] = cur.fetchone()[0]

    for c in columns:
        new_board = board_id_map.get(c["BoardId"])
        if new_board is None:
            continue
        cur.execute(
            "INSERT INTO tblColumns (BoardId, ColumnName, Position, MaxTaskLimit) VALUES (?, ?, ?, ?)",
            new_board, c["ColumnName"] or "", c["Position"], c["MaxTaskLimit"],
        )

    for t in tasks:
        new_board = board_id_map.get(t["BoardId"])
        if new_board is None:
            continue
        cur.execute(
            "INSERT INTO tblTasks (BoardId, Title, Description, Category, ColumnIndex, ColorKey, Tags,"
            " DateCreated, DueDate, StartDate, FinishDate, TimeDue, ReminderTime)"
            " VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            new_board, t["Title"] or "", t["Description"] or "", t["Category"] or "",
            t["ColumnIndex"], t["ColorKey"] or "", t["Tags"] or "",
            t["DateCreated"] or "", t["DueDate"] or "", t["StartDate"] or "",
            t["FinishDate"] or "", t["TimeDue"] or "", t["ReminderTime"] or "",
        )

    dst.commit()
    print(f"Migrated {len(board_id_map)} boards (new IDs: {board_id_map}) with their columns and tasks.")
    src.close()
    dst.close()


if __name__ == "__main__":
    main()
