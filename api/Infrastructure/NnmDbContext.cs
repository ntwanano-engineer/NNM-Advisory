using Microsoft.EntityFrameworkCore;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Infrastructure;

/// <summary>
/// EF Core database context for the NNM Advisory application. Registers all
/// seven entities, applies the column-level schema, and seeds baseline
/// company content, service blocks, and leadership profiles.
/// Requirements 11.1, 11.2, 11.3.
/// </summary>
public sealed class NnmDbContext : DbContext
{
    public NnmDbContext(DbContextOptions<NnmDbContext> options) : base(options) { }

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<CompanyContent> CompanyContents => Set<CompanyContent>();
    public DbSet<ServiceBlock> ServiceBlocks => Set<ServiceBlock>();
    public DbSet<LeadershipProfile> LeadershipProfiles => Set<LeadershipProfile>();
    public DbSet<ContactSubmission> ContactSubmissions => Set<ContactSubmission>();
    public DbSet<CustomerSurveyResponse> CustomerSurveyResponses => Set<CustomerSurveyResponse>();
    public DbSet<EmployeeSurveyResponse> EmployeeSurveyResponses => Set<EmployeeSurveyResponse>();

    private static readonly System.DateTimeOffset SeedTimestamp =
        new(2026, 11, 1, 0, 0, 0, System.TimeSpan.Zero);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureAdminUsers(modelBuilder);
        ConfigureCompanyContent(modelBuilder);
        ConfigureServiceBlocks(modelBuilder);
        ConfigureLeadershipProfiles(modelBuilder);
        ConfigureContactSubmissions(modelBuilder);
        ConfigureCustomerSurveyResponses(modelBuilder);
        ConfigureEmployeeSurveyResponses(modelBuilder);
    }

    private static void ConfigureAdminUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("admin_users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Username).IsRequired().HasMaxLength(254);
            entity.HasIndex(e => e.Username).IsUnique().HasDatabaseName("ix_admin_users_username");
            entity.Property(e => e.PasswordHash).IsRequired().HasColumnType("text");
            entity.Property(e => e.FailedLoginAttempts).IsRequired().HasDefaultValue(0);
            entity.Property(e => e.LockedUntil).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnType("timestamptz").HasDefaultValueSql("now()");
            entity.Property(e => e.UpdatedAt).IsRequired().HasColumnType("timestamptz").HasDefaultValueSql("now()");
        });
    }

    private static void ConfigureCompanyContent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyContent>(entity =>
        {
            entity.ToTable("company_content");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.ContentKey).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.ContentKey).IsUnique().HasDatabaseName("ix_company_content_content_key");
            entity.Property(e => e.ContentValue).IsRequired().HasColumnType("text");
            entity.Property(e => e.UpdatedAt).IsRequired().HasColumnType("timestamptz").HasDefaultValueSql("now()");
            entity.HasData(
                new CompanyContent { Id = new System.Guid("b2e1d4f3-0000-4000-8000-000000000001"), ContentKey = "tagline", ContentValue = "Accounting and technology advisory that helps your business run smoothly and grow with confidence.", UpdatedAt = SeedTimestamp },
                new CompanyContent { Id = new System.Guid("b2e1d4f3-0000-4000-8000-000000000002"), ContentKey = "hero_cta_label", ContentValue = "Contact Us", UpdatedAt = SeedTimestamp },
                new CompanyContent { Id = new System.Guid("b2e1d4f3-0000-4000-8000-000000000003"), ContentKey = "mission_heading", ContentValue = "Helping businesses run smoothly and grow with confidence.", UpdatedAt = SeedTimestamp },
                new CompanyContent { Id = new System.Guid("b2e1d4f3-0000-4000-8000-000000000004"), ContentKey = "mission_statement", ContentValue = "Our mission is to give small and growing businesses the financial clarity and modern technology they need to make confident decisions.", UpdatedAt = SeedTimestamp },
                new CompanyContent { Id = new System.Guid("b2e1d4f3-0000-4000-8000-000000000005"), ContentKey = "mission_body", ContentValue = "NNM Advisory brings together qualified chartered-accounting expertise and hands-on technology under one roof. We pair dependable bookkeeping, tax, and reporting with practical IT - payroll and HR systems, websites, cloud, and AI-enabled solutions - so our clients get a single trusted partner for both their numbers and the systems that run them. We believe good financial information and well-run technology should be within reach of every business, not just large ones, and we explain our work in plain language so you always understand the decisions behind it.", UpdatedAt = SeedTimestamp },
                new CompanyContent { Id = new System.Guid("b2e1d4f3-0000-4000-8000-000000000006"), ContentKey = "it_ai_cloud_heading", ContentValue = "How we implement solutions with AI and cloud technology", UpdatedAt = SeedTimestamp },
                new CompanyContent { Id = new System.Guid("b2e1d4f3-0000-4000-8000-000000000007"), ContentKey = "it_ai_cloud_body", ContentValue = "We design and deliver solutions cloud-first - on AWS and Azure - using infrastructure-as-code so environments are repeatable, secure, and cost-aware. We build and automate CI/CD pipelines for reliable, low-risk releases, and we apply AI pragmatically: automating repetitive finance and operations tasks, surfacing insights from your data, and adding assistants and document-processing where they genuinely save time. Every solution is built on secure, testable, maintainable foundations and sized to your business, so technology becomes a durable advantage rather than an overhead.", UpdatedAt = SeedTimestamp });
        });
    }

    private static void ConfigureServiceBlocks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceBlock>(entity =>
        {
            entity.ToTable("service_blocks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Category).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Category).IsUnique().HasDatabaseName("ix_service_blocks_category");
            entity.Property(e => e.Title).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).IsRequired().HasColumnType("text");
            entity.Property(e => e.Offerings).HasColumnType("text[]");
            entity.Property(e => e.DisplayOrder).IsRequired().HasDefaultValue(0);
            entity.Property(e => e.UpdatedAt).IsRequired().HasColumnType("timestamptz").HasDefaultValueSql("now()");
            entity.HasData(
                new ServiceBlock { Id = new System.Guid("c3f2e5a4-0000-4000-8000-000000000001"), Category = "accounting", Title = "Accounting Services", Description = "End-to-end accounting support that keeps your finances accurate, compliant, and ready for decision-making.", Offerings = new[] { "Bookkeeping", "Tax services", "Financial analysis and insights" }, DisplayOrder = 0, UpdatedAt = SeedTimestamp },
                new ServiceBlock { Id = new System.Guid("c3f2e5a4-0000-4000-8000-000000000002"), Category = "it", Title = "IT Services", Description = "Practical technology services that set up and support the systems your business relies on every day.", Offerings = new[] { "Financial system setup for payroll and HR", "Website development", "IT infrastructure" }, DisplayOrder = 1, UpdatedAt = SeedTimestamp },
                new ServiceBlock { Id = new System.Guid("c3f2e5a4-0000-4000-8000-000000000003"), Category = "customer_survey", Title = "Customer Surveys", Description = "Structured customer surveys that capture satisfaction and loyalty so you can act on what your customers value most.", Offerings = System.Array.Empty<string>(), DisplayOrder = 2, UpdatedAt = SeedTimestamp },
                new ServiceBlock { Id = new System.Guid("c3f2e5a4-0000-4000-8000-000000000004"), Category = "employee_survey", Title = "Employee Surveys", Description = "Anonymous employee surveys that surface engagement and workplace sentiment to help you build a stronger team.", Offerings = System.Array.Empty<string>(), DisplayOrder = 3, UpdatedAt = SeedTimestamp });
        });
    }

    private static void ConfigureLeadershipProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LeadershipProfile>(entity =>
        {
            entity.ToTable("leadership_profiles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Biography).IsRequired().HasColumnType("text");
            entity.ToTable(t => t.HasCheckConstraint("chk_leadership_profiles_biography_length", "char_length(biography) <= 2000"));
            entity.Property(e => e.PhotoS3Key).HasMaxLength(500);
            entity.Property(e => e.DisplayOrder).IsRequired().HasDefaultValue(0);
            entity.Property(e => e.UpdatedAt).IsRequired().HasColumnType("timestamptz").HasDefaultValueSql("now()");
            entity.HasData(
                new LeadershipProfile { Id = new System.Guid("a1f0c3e2-0000-4000-8000-000000000001"), FullName = "Ntwanano Mabasa", Title = "Chief Operating Officer (COO)", Biography = "Ntwanano Trevor Mabasa is Chief Operating Officer of NNM Advisory, leading day-to-day operations across the firm's accounting and IT service lines. He is a Senior Software Engineer with deep experience in backend development (.NET and Python) and modern cloud and DevOps practice. Education: BSc Honours in Computer Science (University of Johannesburg, 2015–2016) and a BSc in Mathematical Sciences, majoring in Mathematics and Computer Science (University of Johannesburg, 2011–2014). Experience: he is currently a Senior Software Engineer at Rand Merchant Bank (RMB), and has previously held senior engineering roles at Old Mutual, FNB South Africa, and African Bank, among others. His work spans implementing complex business solutions, building cloud-based and enterprise applications, and site-reliability engineering - provisioning environments on AWS using infrastructure-as-code (CloudFormation) and building CI/CD pipelines on both AWS and Azure. Certifications include Python Programming for AWS, Unit Testing .NET with xUnit and Moq, and Microsoft Certified Professional, and he is working toward AWS Developer certification. At NNM Advisory he brings this engineering and cloud expertise to the technology side of the practice, ensuring dependable, secure, and scalable solutions for every client.", PhotoS3Key = "leadership/ntwanano-mabasa.jpeg", DisplayOrder = 0, UpdatedAt = SeedTimestamp },
                new LeadershipProfile { Id = new System.Guid("a1f0c3e2-0000-4000-8000-000000000002"), FullName = "Ntshovelo Priscilla Novela", Title = "Chief Information Officer (CIO)", Biography = "Ntshovelo Priscilla Novela is a qualified Chartered Accountant, CA(SA), and a founder of NNM Advisory, leading the firm's accounting, reporting, and financial-controls practice. Education: she passed the SAICA Assessment of Professional Competence (APC) in December 2024 and the Initial Test of Competence (ITC) in June 2023; holds a Post Graduate Diploma in Accountancy (CTA) from UNISA (2022, with Golden Key recognition in 2020); and a Bachelor of Accounting Sciences from WITS University (2018–2020). Experience: she is currently a Senior Accountant at Tourvest Travel Services (2026–present), where she manages monthly reporting, reconciliations, cash flow, and a team of accountants. She trained as an Audit Senior at Deloitte (2022–2026), managing audits from planning to completion across high-risk and complex IFRS areas for listed, private, and state-owned entities, including Powerfleet, Africa Data Centers, Merafe, Tiger Brands, Motus, Yokohama, AECI, CA&S Holdings, and Eskom. Her areas of expertise include IFRS reporting, balance-sheet substantiation, financial controls, management and regulatory reporting, risk assessment, and financial analysis.", PhotoS3Key = "leadership/ntshovelo-priscilla-novela.jpeg", DisplayOrder = 1, UpdatedAt = SeedTimestamp });
        });
    }

    private static void ConfigureContactSubmissions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContactSubmission>(entity =>
        {
            entity.ToTable("contact_submissions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(254);
            entity.Property(e => e.Subject).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Message).IsRequired().HasColumnType("text");
            entity.ToTable(t => t.HasCheckConstraint("chk_contact_submissions_message_length", "char_length(message) BETWEEN 10 AND 2000"));
            entity.Property(e => e.SubmittedAt).IsRequired().HasColumnType("timestamptz").HasDefaultValueSql("now()");
        });
    }

    private static void ConfigureCustomerSurveyResponses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerSurveyResponse>(entity =>
        {
            entity.ToTable("customer_survey_responses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.ServiceSatisfaction).IsRequired().HasColumnType("smallint");
            entity.ToTable(t => t.HasCheckConstraint("chk_customer_survey_service_satisfaction", "service_satisfaction BETWEEN 1 AND 5"));
            entity.Property(e => e.NpsScore).IsRequired().HasColumnType("smallint");
            entity.ToTable(t => t.HasCheckConstraint("chk_customer_survey_nps_score", "nps_score BETWEEN 0 AND 10"));
            entity.Property(e => e.OpenFeedback).HasColumnType("text");
            entity.ToTable(t => t.HasCheckConstraint("chk_customer_survey_open_feedback_length", "open_feedback IS NULL OR char_length(open_feedback) <= 1000"));
            entity.Property(e => e.SubmittedAt).IsRequired().HasColumnType("timestamptz").HasDefaultValueSql("now()");
        });
    }

    private static void ConfigureEmployeeSurveyResponses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmployeeSurveyResponse>(entity =>
        {
            entity.ToTable("employee_survey_responses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.EngagementScore).IsRequired().HasColumnType("smallint");
            entity.Property(e => e.WorkplaceSatisfaction).IsRequired().HasColumnType("text");
            entity.Property(e => e.OpenFeedback).HasColumnType("text");
            entity.Property(e => e.SubmittedAt).IsRequired().HasColumnType("timestamptz").HasDefaultValueSql("now()");
        });
    }
}
