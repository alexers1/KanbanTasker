using KanbanTasker.Model.Dto;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KanbanTasker.Model.Services
{
    /// <summary>
    /// Board/column operations. Async because every implementation crosses an I/O boundary:
    /// EF Core → SQL Server on the API side, HttpClient → REST on the WinUI client side.
    /// Validate* stays synchronous (pure in-memory validation, no I/O).
    /// </summary>
    public interface IBoardServices
    {
        Task<RowOpResult<BoardDTO>> SaveBoardAsync(BoardDTO board);
        Task<RowOpResult> DeleteBoardAsync(int boardId);
        Task<List<BoardDTO>> GetBoardsAsync();
        RowOpResult<BoardDTO> ValidateBoard(RowOpResult<BoardDTO> result);
        Task<RowOpResult> CreateColumnsAsync(int boardId);
        Task<RowOpResult<ColumnDTO>> SaveColumnAsync(ColumnDTO column);
        Task<List<ColumnDTO>> GetColumnsAsync(int boardId);
        Task<ColumnDTO> CreateColumnAsync(ColumnDTO column);
        Task<RowOpResult> DeleteColumnAsync(ColumnDTO column);
        Task<RowOpResult> UpdateColumnIndexAsync(ColumnDTO column);
    }
}
