using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Orbital.Models;

namespace Orbital.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Domain DbSets
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GroupMessage> GroupMessages => Set<GroupMessage>();
    public DbSet<CallSession> CallSessions => Set<CallSession>();
    public DbSet<UserPublicKey> UserPublicKeys => Set<UserPublicKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // -------------------------------------------------------------
        // 2. ApplicationUser Configuration
        // -------------------------------------------------------------
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => new { u.UserHandle, u.Realm })
                  .IsUnique();

            entity.Property(u => u.UserHandle)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.Property(u => u.Realm)
                  .IsRequired()
                  .HasMaxLength(20);

            entity.Property(u => u.DisplayName)
                  .IsRequired()
                  .HasMaxLength(100);
        });

        // -------------------------------------------------------------
        // 3. RefreshToken Configuration
        // -------------------------------------------------------------
        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(rt => rt.Id);

            entity.HasIndex(rt => rt.Token)
                  .IsUnique();

            entity.HasOne(rt => rt.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(rt => rt.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // -------------------------------------------------------------
        // 4. Friendship Configuration
        // -------------------------------------------------------------
        builder.Entity<Friendship>(entity =>
        {
            entity.HasKey(f => f.Id);

            entity.HasIndex(f => new { f.RequesterId, f.AddresseeId })
                  .IsUnique();

            entity.HasOne(f => f.Requester)
                  .WithMany()
                  .HasForeignKey(f => f.RequesterId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(f => f.Addressee)
                  .WithMany()
                  .HasForeignKey(f => f.AddresseeId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // -------------------------------------------------------------
        // 5. DirectMessage Configuration
        // -------------------------------------------------------------
        builder.Entity<DirectMessage>(entity =>
        {
            entity.HasKey(dm => dm.Id);

            entity.Property(dm => dm.Content)
                  .HasMaxLength(4000);

            entity.Property(dm => dm.Ciphertext)
                  .HasMaxLength(8000);

            entity.Property(dm => dm.Nonce)
                  .HasMaxLength(100);

            entity.HasOne(dm => dm.Sender)
                  .WithMany()
                  .HasForeignKey(dm => dm.SenderId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(dm => dm.Recipient)
                  .WithMany()
                  .HasForeignKey(dm => dm.RecipientId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(dm => new { dm.SenderId, dm.RecipientId, dm.SentAt });
        });

        // -------------------------------------------------------------
        // 6. Group & GroupMember Configuration
        // -------------------------------------------------------------
        builder.Entity<Group>(entity =>
        {
            entity.HasKey(g => g.Id);

            entity.Property(g => g.Name)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.HasOne(g => g.CreatedBy)
                  .WithMany()
                  .HasForeignKey(g => g.CreatedById)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(gm => new { gm.GroupId, gm.UserId });

            entity.HasOne(gm => gm.Group)
                  .WithMany(g => g.Members)
                  .HasForeignKey(gm => gm.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(gm => gm.User)
                  .WithMany(u => u.GroupMemberships)
                  .HasForeignKey(gm => gm.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // -------------------------------------------------------------
        // 7. GroupMessage Configuration
        // -------------------------------------------------------------
        builder.Entity<GroupMessage>(entity =>
        {
            entity.HasKey(gm => gm.Id);

            entity.Property(gm => gm.Content)
                  .HasMaxLength(4000);

            entity.HasOne(gm => gm.Group)
                  .WithMany(g => g.Messages)
                  .HasForeignKey(gm => gm.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(gm => gm.Sender)
                  .WithMany()
                  .HasForeignKey(gm => gm.SenderId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(gm => new { gm.GroupId, gm.SentAt });
        });

        // -------------------------------------------------------------
        // 8. CallSession Configuration
        // -------------------------------------------------------------
        builder.Entity<CallSession>(entity =>
        {
            entity.HasKey(cs => cs.Id);

            entity.HasOne(cs => cs.Initiator)
                  .WithMany()
                  .HasForeignKey(cs => cs.InitiatorId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(cs => cs.Peer)
                  .WithMany()
                  .HasForeignKey(cs => cs.PeerId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(cs => cs.Group)
                  .WithMany()
                  .HasForeignKey(cs => cs.GroupId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // -------------------------------------------------------------
        // 9. UserPublicKey Configuration (E2EE)
        // -------------------------------------------------------------
        builder.Entity<UserPublicKey>(entity =>
        {
            entity.HasKey(k => k.UserId);

            entity.Property(k => k.PublicKey)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.HasOne(k => k.User)
                  .WithOne()
                  .HasForeignKey<UserPublicKey>(k => k.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}