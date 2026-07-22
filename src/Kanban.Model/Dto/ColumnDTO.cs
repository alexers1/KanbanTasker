using System;
using System.Collections.Generic;
using System.Text;

namespace KanbanTasker.Model.Dto
{
    /// <summary>
    /// A data transfer object for column data
    /// </summary>
    public class ColumnDTO : IStamped
    {
        public int Id { get; set; }
        public int BoardId { get; set; }
        public string ColumnName { get; set; } = "";
        public int Position { get; set; }
        public int MaxTaskLimit { get; set; }

        // Stable identity + timestamps for idempotent import (see IStamped).
        public Guid Uuid { get; set; }
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }
}