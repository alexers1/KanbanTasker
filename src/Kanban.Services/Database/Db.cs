using KanbanTasker.Model.Dto;
using Microsoft.EntityFrameworkCore;
using System;

namespace KanbanTasker.Services.Database
{
    public class Db : DbContext
    {
        public DbSet<BoardDTO> Boards { get; set; }
        public DbSet<TaskDTO> Tasks { get; set; }
        public DbSet<ColumnDTO> Columns { get; set; }

        public Db(DbContextOptions<Db> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<BoardDTO>().ToTable("tblBoards");
            mb.Entity<TaskDTO>().ToTable("tblTasks");
            mb.Entity<ColumnDTO>().ToTable("tblColumns");

            // Add any indexes or constraints if needed for parity with old
            mb.Entity<TaskDTO>()
                .HasOne(t => t.Board)
                .WithMany(b => b.Tasks)
                .HasForeignKey(t => t.BoardId);
        }
    }
}