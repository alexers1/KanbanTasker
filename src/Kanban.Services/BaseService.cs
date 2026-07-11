using KanbanTasker.Model;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace KanbanTasker.Services
{
    public abstract class BaseService
    {
        protected Database.Db db { get; set; }

        public BaseService(Database.Db db)
        {
            this.db = db;
        }

        public virtual RowOpResult<BoardDTO> ValidateBoard(RowOpResult<BoardDTO> result)
        {
            BoardDTO b = result.Entity;

            if (string.IsNullOrEmpty(b.Name))
                result.ErrorMessage = "Name is required.";
            else if (b.Name.Length > 100)
                result.ErrorMessage = "Name is too long.";
            if (string.IsNullOrEmpty(b.Notes))
                result.ErrorMessage = "Notes are required.";
            else if (b.Notes.Length > 1000)
                result.ErrorMessage = "Notes are too long.";

            result.Success = string.IsNullOrEmpty(result.ErrorMessage);
            return result;
        }

        public virtual RowOpResult<TaskDTO> ValidateTask(RowOpResult<TaskDTO> result)
        {
            TaskDTO t = result.Entity;

            if (string.IsNullOrEmpty(t.Title))
                t.Title = "";
            else if (t.Title.Length > 100)
                result.ErrorMessage = "Title is too long.";
            if (string.IsNullOrEmpty(t.Description))
                t.Description = "";
            if (string.IsNullOrEmpty(t.DueDate))
                t.DueDate = "";
            if (string.IsNullOrEmpty(t.FinishDate))
                t.FinishDate = "";
            if (string.IsNullOrEmpty(t.ReminderTime))
                t.ReminderTime = "";
            if (string.IsNullOrEmpty(t.StartDate))
                t.StartDate = "";

            result.Success = string.IsNullOrEmpty(result.ErrorMessage);
            return result;
        }
    }
}