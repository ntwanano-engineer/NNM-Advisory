using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NnmAdvisory.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedLeadershipProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "leadership_profiles",
                columns: new[] { "id", "biography", "full_name", "photo_s3_key", "title", "updated_at" },
                values: new object[] { new Guid("a1f0c3e2-0000-4000-8000-000000000001"), "Ntwanano Mabasa is the Chief Operating Officer of NNM Advisory, where he leads day-to-day operations across the firm's accounting and IT service lines. He is responsible for operational strategy, service delivery, and client relationships, ensuring NNM Advisory's teams deliver dependable, high-quality outcomes for every engagement. He works closely with the leadership team to align the firm's people, processes, and technology with the needs of the businesses NNM Advisory serves.", "Ntwanano Mabasa", "leadership/ntwanano-mabasa.jpeg", "Chief Operating Officer (COO)", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.InsertData(
                table: "leadership_profiles",
                columns: new[] { "id", "biography", "display_order", "full_name", "photo_s3_key", "title", "updated_at" },
                values: new object[] { new Guid("a1f0c3e2-0000-4000-8000-000000000002"), "Ntshovelo Priscilla Novela is the Chief Information Officer of NNM Advisory and a qualified Chartered Accountant, CA(SA). She passed the SAICA Assessment of Professional Competence (APC) in December 2024 and the Initial Test of Competence (ITC) in June 2023. She holds a Post Graduate Diploma in Accountancy (CTA) from UNISA (2022) and a Bachelor of Accounting Sciences from WITS University (2018–2020). Ntshovelo was formerly an Audit Senior at Deloitte (February 2022 – March 2026), where she managed audits for JSE- and NYSE-listed entities including Powerfleet, Africa Data Centers, Merafe, Tiger Brands, Motus, AECI, and Eskom. She is currently a Senior Accountant at Tourvest Travel Services (April 2026 – present). Her areas of expertise include IFRS reporting, balance sheet substantiation, financial controls, management reporting, risk assessment, and financial analysis.", 1, "Ntshovelo Priscilla Novela", "leadership/ntshovelo-priscilla-novela.jpeg", "Chief Information Officer (CIO)", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "leadership_profiles",
                keyColumn: "id",
                keyValue: new Guid("a1f0c3e2-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "leadership_profiles",
                keyColumn: "id",
                keyValue: new Guid("a1f0c3e2-0000-4000-8000-000000000002"));
        }
    }
}
