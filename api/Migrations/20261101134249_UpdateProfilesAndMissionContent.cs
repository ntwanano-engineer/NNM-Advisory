using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NnmAdvisory.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateProfilesAndMissionContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "company_content",
                columns: new[] { "id", "content_key", "content_value", "updated_at" },
                values: new object[,]
                {
                    { new Guid("b2e1d4f3-0000-4000-8000-000000000003"), "mission_heading", "Helping businesses run smoothly and grow with confidence.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b2e1d4f3-0000-4000-8000-000000000004"), "mission_statement", "Our mission is to give small and growing businesses the financial clarity and modern technology they need to make confident decisions.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b2e1d4f3-0000-4000-8000-000000000005"), "mission_body", "NNM Advisory brings together qualified chartered-accounting expertise and hands-on technology under one roof. We pair dependable bookkeeping, tax, and reporting with practical IT - payroll and HR systems, websites, cloud, and AI-enabled solutions - so our clients get a single trusted partner for both their numbers and the systems that run them. We believe good financial information and well-run technology should be within reach of every business, not just large ones, and we explain our work in plain language so you always understand the decisions behind it.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b2e1d4f3-0000-4000-8000-000000000006"), "it_ai_cloud_heading", "How we implement solutions with AI and cloud technology", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b2e1d4f3-0000-4000-8000-000000000007"), "it_ai_cloud_body", "We design and deliver solutions cloud-first - on AWS and Azure - using infrastructure-as-code so environments are repeatable, secure, and cost-aware. We build and automate CI/CD pipelines for reliable, low-risk releases, and we apply AI pragmatically: automating repetitive finance and operations tasks, surfacing insights from your data, and adding assistants and document-processing where they genuinely save time. Every solution is built on secure, testable, maintainable foundations and sized to your business, so technology becomes a durable advantage rather than an overhead.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.UpdateData(
                table: "leadership_profiles",
                keyColumn: "id",
                keyValue: new Guid("a1f0c3e2-0000-4000-8000-000000000001"),
                column: "biography",
                value: "Ntwanano Trevor Mabasa is Chief Operating Officer of NNM Advisory, leading day-to-day operations across the firm's accounting and IT service lines. He is a Senior Software Engineer with deep experience in backend development (.NET and Python) and modern cloud and DevOps practice. Education: BSc Honours in Computer Science (University of Johannesburg, 2015–2016) and a BSc in Mathematical Sciences, majoring in Mathematics and Computer Science (University of Johannesburg, 2011–2014). Experience: he is currently a Senior Software Engineer at Rand Merchant Bank (RMB), and has previously held senior engineering roles at Old Mutual, FNB South Africa, and African Bank, among others. His work spans implementing complex business solutions, building cloud-based and enterprise applications, and site-reliability engineering - provisioning environments on AWS using infrastructure-as-code (CloudFormation) and building CI/CD pipelines on both AWS and Azure. Certifications include Python Programming for AWS, Unit Testing .NET with xUnit and Moq, and Microsoft Certified Professional, and he is working toward AWS Developer certification. At NNM Advisory he brings this engineering and cloud expertise to the technology side of the practice, ensuring dependable, secure, and scalable solutions for every client.");

            migrationBuilder.UpdateData(
                table: "leadership_profiles",
                keyColumn: "id",
                keyValue: new Guid("a1f0c3e2-0000-4000-8000-000000000002"),
                column: "biography",
                value: "Ntshovelo Priscilla Novela is a qualified Chartered Accountant, CA(SA), and a founder of NNM Advisory, leading the firm's accounting, reporting, and financial-controls practice. Education: she passed the SAICA Assessment of Professional Competence (APC) in December 2024 and the Initial Test of Competence (ITC) in June 2023; holds a Post Graduate Diploma in Accountancy (CTA) from UNISA (2022, with Golden Key recognition in 2020); and a Bachelor of Accounting Sciences from WITS University (2018–2020). Experience: she is currently a Senior Accountant at Tourvest Travel Services (2026–present), where she manages monthly reporting, reconciliations, cash flow, and a team of accountants. She trained as an Audit Senior at Deloitte (2022–2026), managing audits from planning to completion across high-risk and complex IFRS areas for listed, private, and state-owned entities, including Powerfleet, Africa Data Centers, Merafe, Tiger Brands, Motus, Yokohama, AECI, CA&S Holdings, and Eskom. Her areas of expertise include IFRS reporting, balance-sheet substantiation, financial controls, management and regulatory reporting, risk assessment, and financial analysis.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "company_content",
                keyColumn: "id",
                keyValue: new Guid("b2e1d4f3-0000-4000-8000-000000000003"));

            migrationBuilder.DeleteData(
                table: "company_content",
                keyColumn: "id",
                keyValue: new Guid("b2e1d4f3-0000-4000-8000-000000000004"));

            migrationBuilder.DeleteData(
                table: "company_content",
                keyColumn: "id",
                keyValue: new Guid("b2e1d4f3-0000-4000-8000-000000000005"));

            migrationBuilder.DeleteData(
                table: "company_content",
                keyColumn: "id",
                keyValue: new Guid("b2e1d4f3-0000-4000-8000-000000000006"));

            migrationBuilder.DeleteData(
                table: "company_content",
                keyColumn: "id",
                keyValue: new Guid("b2e1d4f3-0000-4000-8000-000000000007"));

            migrationBuilder.UpdateData(
                table: "leadership_profiles",
                keyColumn: "id",
                keyValue: new Guid("a1f0c3e2-0000-4000-8000-000000000001"),
                column: "biography",
                value: "Ntwanano Mabasa is the Chief Operating Officer of NNM Advisory, where he leads day-to-day operations across the firm's accounting and IT service lines. He is responsible for operational strategy, service delivery, and client relationships, ensuring NNM Advisory's teams deliver dependable, high-quality outcomes for every engagement. He works closely with the leadership team to align the firm's people, processes, and technology with the needs of the businesses NNM Advisory serves.");

            migrationBuilder.UpdateData(
                table: "leadership_profiles",
                keyColumn: "id",
                keyValue: new Guid("a1f0c3e2-0000-4000-8000-000000000002"),
                column: "biography",
                value: "Ntshovelo Priscilla Novela is the Chief Information Officer of NNM Advisory and a qualified Chartered Accountant, CA(SA). She passed the SAICA Assessment of Professional Competence (APC) in December 2024 and the Initial Test of Competence (ITC) in June 2023. She holds a Post Graduate Diploma in Accountancy (CTA) from UNISA (2022) and a Bachelor of Accounting Sciences from WITS University (2018–2020). Ntshovelo was formerly an Audit Senior at Deloitte (February 2022 – March 2026), where she managed audits for JSE- and NYSE-listed entities including Powerfleet, Africa Data Centers, Merafe, Tiger Brands, Motus, AECI, and Eskom. She is currently a Senior Accountant at Tourvest Travel Services (April 2026 – present). Her areas of expertise include IFRS reporting, balance sheet substantiation, financial controls, management reporting, risk assessment, and financial analysis.");
        }
    }
}
