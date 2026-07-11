using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using KanbanTasker.Model;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;
using KanbanTasker.Services.Database;

namespace KanbanTasker.Services.SqlServer
{
    public class BoardServices : BaseService, IBoardServices
    {
        public BoardServices(Database.Db db) : base(db)
        {
        }

        public List<BoardDTO> GetBoards()
        {
            var boards = db.Boards
                .Include(b => b.Tasks)
                .AsNoTracking()
                .ToList();

            return boards;
        }

        public RowOpResult<BoardDTO> SaveBoard(BoardDTO board)
        {
            RowOpResult<BoardDTO> result = new RowOpResult<BoardDTO>(board);

            ValidateBoard(result);

            if (!result.Success)
                return result;

            if (board.Id == 0)
            {
                db.Boards.Add(board);
            }
            else
            {
                db.Boards.Update(board);
            }

            db.SaveChanges();
            result.Success = true;
            return result;
        }

        public RowOpResult DeleteBoard(int boardId)
        {
            RowOpResult result = new RowOpResult();

            var board = db.Boards
                .Include(b => b.Tasks)
                .FirstOrDefault(b => b.Id == boardId);

            if (board != null)
            {
                // Delete related columns and tasks (cascade in old raw SQL)
                var columns = db.Columns.Where(c => c.BoardId == boardId).ToList();
                db.Columns.RemoveRange(columns);

                if (board.Tasks != null)
                    db.Tasks.RemoveRange(board.Tasks);

                db.Boards.Remove(board);
                db.SaveChanges();
            }

            result.Success = true;
            return result;
        }

        public RowOpResult CreateColumns(int boardId)
        {
            RowOpResult result = new RowOpResult();

            // Check if columns already exist
            if (db.Columns.Any(c => c.BoardId == boardId))
            {
                result.Success = true;
                return result;
            }

            var defaultColumns = new List<ColumnDTO>
            {
                new ColumnDTO { BoardId = boardId, ColumnName = "Backlog", Position = 0, MaxTaskLimit = 10 },
                new ColumnDTO { BoardId = boardId, ColumnName = "To Do", Position = 1, MaxTaskLimit = 10 },
                new ColumnDTO { BoardId = boardId, ColumnName = "In Progress", Position = 2, MaxTaskLimit = 10 },
                new ColumnDTO { BoardId = boardId, ColumnName = "Review", Position = 3, MaxTaskLimit = 10 },
                new ColumnDTO { BoardId = boardId, ColumnName = "Completed", Position = 4, MaxTaskLimit = 10 }
            };

            db.Columns.AddRange(defaultColumns);
            db.SaveChanges();

            result.Success = true;
            return result;
        }

        public List<ColumnDTO> GetColumns(int boardId)
        {
            return db.Columns
                .Where(c => c.BoardId == boardId)
                .OrderBy(c => c.Position)
                .AsNoTracking()
                .ToList();
        }

        public RowOpResult<ColumnDTO> SaveColumn(ColumnDTO column)
        {
            RowOpResult<ColumnDTO> result = new RowOpResult<ColumnDTO>(column);

            if (column.Id == 0)
            {
                db.Columns.Add(column);
            }
            else
            {
                db.Columns.Update(column);
            }

            db.SaveChanges();
            result.Success = true;
            return result;
        }

        public ColumnDTO CreateColumn(ColumnDTO column)
        {
            db.Columns.Add(column);
            db.SaveChanges();
            return column;
        }

        public RowOpResult DeleteColumn(ColumnDTO column)
        {
            RowOpResult result = new RowOpResult();

            var col = db.Columns.FirstOrDefault(c => c.Id == column.Id && c.BoardId == column.BoardId);
            if (col != null)
            {
                // Delete tasks in that column (by Category, as in old code)
                var tasks = db.Tasks.Where(t => t.Category == col.ColumnName && t.BoardId == col.BoardId).ToList();
                db.Tasks.RemoveRange(tasks);

                db.Columns.Remove(col);
                db.SaveChanges();
            }

            result.Success = true;
            return result;
        }

        public RowOpResult UpdateColumnIndex(ColumnDTO column)
        {
            RowOpResult result = new RowOpResult();

            var existing = db.Columns.FirstOrDefault(c => c.Id == column.Id);
            if (existing != null)
            {
                existing.Position = column.Position;
                db.SaveChanges();
            }

            result.Success = true;
            return result;
        }

        // Note: BackupDB is provided via IDatabaseServices / DatabaseServices (separate concern, not on IBoardServices).
    }
}