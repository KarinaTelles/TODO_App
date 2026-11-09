using Microsoft.EntityFrameworkCore;

namespace TaskAssistant.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<PendingAction> PendingActions => Set<PendingAction>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TaskItem>(e =>
        {
            e.ToTable("tasks");
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.Notes).HasMaxLength(2000);
            e.Property(t => t.Priority).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Version).IsConcurrencyToken();
            e.HasIndex(t => new { t.Done, t.DueDate });
        });

        model.Entity<Conversation>(e => e.ToTable("conversations"));

        model.Entity<PendingAction>(e =>
        {
            e.ToTable("pending_actions");
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(a => a.ToolUseId).IsRequired();
            e.HasIndex(a => new { a.ConversationId, a.Status });
            e.HasOne<Conversation>().WithMany().HasForeignKey(a => a.ConversationId);
        });
    }
}
