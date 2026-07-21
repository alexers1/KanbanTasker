using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using KanbanTasker.Model;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;

namespace KanbanTasker.Services.SqlServer
{
    public class BoardServices : BaseService, IBoardServices
    {
        public BoardServices(Database.Db db) : base(db)
        {
        }

        public async Task<List<BoardDTO>> GetBoardsAsync()
        {
            return await db.Boards
                .Include(b => b.Tasks)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<RowOpResult<BoardDTO>> SaveBoardAsync(BoardDTO board)
        {
            var result = new RowOpResult<BoardDTO>(board);

            ValidateBoard(result);
            if (!result.Success)
                return result;

            if (board.Id == 0)
                db.Boards.Add(board);
            else
                db.Boards.Update(board);

            await db.SaveChangesAsync();
            result.Success = true;
            return result;
        }

        public async Task<RowOpResult> DeleteBoardAsync(int boardId)
        {
            var result = new RowOpResult();

            var board = await db.Boards
                .Include(b => b.Tasks)
                .FirstOrDefaultAsync(b => b.Id == boardId);

            if (board != null)
            {
                // Delete related columns and tasks (cascade in old raw SQL)
                var columns = await db.Columns.Where(c => c.BoardId == boardId).ToListAsync();
                db.Columns.RemoveRange(columns);

                if (board.Tasks != null)
                    db.Tasks.RemoveRange(board.Tasks);

                db.Boards.Remove(board);
                await db.SaveChangesAsync();
            }

            result.Success = true;
            return result;
        }

        public async Task<RowOpResult> CreateColumnsAsync(int boardId)
        {
            var result = new RowOpResult();

            if (await db.Columns.AnyAsync(c => c.BoardId == boardId))
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
            await db.SaveChangesAsync();

            result.Success = true;
            return result;
        }

        public async Task<List<ColumnDTO>> GetColumnsAsync(int boardId)
        {
            return await db.Columns
                .Where(c => c.BoardId == boardId)
                .OrderBy(c => c.Position)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<RowOpResult<ColumnDTO>> SaveColumnAsync(ColumnDTO column)
        {
            var result = new RowOpResult<ColumnDTO>(column);

            if (column.Id == 0)
            {
                db.Columns.Add(column);
                await db.SaveChangesAsync();
            }
            else
            {
                // Read the current name first so a rename can cascade to the column's
                // tasks (their Category). This used to be a per-task loop in the WinUI
                // client; the server now does it in one step.
                var oldName = await db.Columns
                    .AsNoTracking()
                    .Where(c => c.Id == column.Id)
                    .Select(c => c.ColumnName)
                    .FirstOrDefaultAsync();

                db.Columns.Update(column);
                await db.SaveChangesAsync();

                if (oldName != null && oldName != column.ColumnName)
                {
                    var tasks = await db.Tasks
                        .Where(t => t.BoardId == column.BoardId && t.Category == oldName)
                        .ToListAsync();
                    foreach (var t in tasks)
                        t.Category = column.ColumnName;
                    await db.SaveChangesAsync();
                }
            }

            result.Success = true;
            return result;
        }

        public async Task<ColumnDTO> CreateColumnAsync(ColumnDTO column)
        {
            db.Columns.Add(column);
            await db.SaveChangesAsync();
            return column;
        }

        public async Task<RowOpResult> DeleteColumnAsync(ColumnDTO column)
        {
            var result = new RowOpResult();

            var col = await db.Columns.FirstOrDefaultAsync(c => c.Id == column.Id && c.BoardId == column.BoardId);
            if (col != null)
            {
                // Delete tasks in that column (by Category, as in old code)
                var tasks = await db.Tasks.Where(t => t.Category == col.ColumnName && t.BoardId == col.BoardId).ToListAsync();
                db.Tasks.RemoveRange(tasks);

                db.Columns.Remove(col);
                await db.SaveChangesAsync();

                // Close the gap in the remaining columns' positions. Moved from the
                // WinUI client, which used to shift positions one call at a time.
                var after = await db.Columns
                    .Where(c => c.BoardId == col.BoardId && c.Position > col.Position)
                    .OrderBy(c => c.Position)
                    .ToListAsync();
                foreach (var c in after)
                    c.Position -= 1;
                await db.SaveChangesAsync();
            }

            result.Success = true;
            return result;
        }

        public async Task<RowOpResult> UpdateColumnIndexAsync(ColumnDTO column)
        {
            var result = new RowOpResult();

            var existing = await db.Columns.FirstOrDefaultAsync(c => c.Id == column.Id);
            if (existing != null)
            {
                existing.Position = column.Position;
                await db.SaveChangesAsync();
            }

            result.Success = true;
            return result;
        }
    }
}
