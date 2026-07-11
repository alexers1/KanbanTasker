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
    public class TaskServices : BaseService, ITaskServices
    {
        public TaskServices(Database.Db db) : base(db)
        {
        }

        public List<TaskDTO> GetTasks()
        {
            return db.Tasks
                .AsNoTracking()
                .OrderBy(t => t.ColumnIndex)
                .ToList();
        }

        public RowOpResult<TaskDTO> SaveTask(TaskDTO task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            RowOpResult<TaskDTO> result = new RowOpResult<TaskDTO>(task);

            ValidateTask(result);

            if (!result.Success)
                return result;

            if (task.Id == 0)
            {
                db.Tasks.Add(task);
            }
            else
            {
                db.Tasks.Update(task);
            }

            db.SaveChanges();
            result.Success = true;
            return result;
        }

        public RowOpResult DeleteTask(int id)
        {
            RowOpResult result = new RowOpResult();

            var task = db.Tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                db.Tasks.Remove(task);
                db.SaveChanges();
            }

            result.Success = true;
            return result;
        }

        public void UpdateCardIndex(int id, int currentCardIndex)
        {
            var task = db.Tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                task.ColumnIndex = currentCardIndex;
                db.SaveChanges();
            }
        }

        public void UpdateColumnData(TaskDTO task)
        {
            var existing = db.Tasks.FirstOrDefault(t => t.Id == task.Id);
            if (existing != null)
            {
                existing.Category = task.Category;
                existing.ColumnIndex = task.ColumnIndex;
                db.SaveChanges();
            }
        }

        public void UpdateColumnName(int id, string newColName)
        {
            var task = db.Tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                task.Category = newColName;
                db.SaveChanges();
            }
        }

        public override RowOpResult<TaskDTO> ValidateTask(RowOpResult<TaskDTO> result)
        {
            // Delegate to base
            return base.ValidateTask(result);
        }
    }
}