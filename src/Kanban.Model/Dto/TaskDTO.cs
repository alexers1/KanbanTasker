using System;

namespace KanbanTasker.Model.Dto
{
    // A data transfer object for a task
    public class TaskDTO : IStamped
    {
        public int Id { get; set; }
        public int BoardId { get; set; }           // if every task is associate with a board than this should not be nullable.
        public string DateCreated { get; set; } = "";
        public string DueDate { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string FinishDate { get; set; } = "";
        public string TimeDue { get; set; } = "";
        public string ReminderTime { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public int ColumnIndex { get; set; }
        public string ColorKey { get; set; } = "";
        public string Tags { get; set; } = "";

        /// <summary>
        /// Semicolon-separated attachment file names, relative to the user's
        /// attachments folder. Only paths are stored — never file contents —
        /// so the database stays small and files move easily between computers.
        /// </summary>
        public string Attachments { get; set; } = "";

        // Stable identity + timestamps for idempotent import (see IStamped).
        public Guid Uuid { get; set; }
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";

        // Navigation back to the owning board. Optional on the wire: requests identify
        // the board by BoardId, so the client never sends this (a non-nullable reference
        // here would make ASP.NET model-validation reject every create).
        public virtual BoardDTO? Board { get; set; }
    }
}