using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NnmAdvisory.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedCompanyContentAndServiceBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "company_content",
                columns: new[] { "id", "content_key", "content_value", "updated_at" },
                values: new object[,]
                {
                    { new Guid("b2e1d4f3-0000-4000-8000-000000000001"), "tagline", "Accounting and technology advisory that helps your business run smoothly and grow with confidence.", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b2e1d4f3-0000-4000-8000-000000000002"), "hero_cta_label", "Contact Us", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "service_blocks",
                columns: new[] { "id", "category", "description", "offerings", "title", "updated_at" },
                values: new object[] { new Guid("c3f2e5a4-0000-4000-8000-000000000001"), "accounting", "End-to-end accounting support that keeps your finances accurate, compliant, and ready for decision-making.", new[] { "Bookkeeping", "Tax services", "Financial analysis and insights" }, "Accounting Services", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.InsertData(
                table: "service_blocks",
                columns: new[] { "id", "category", "description", "display_order", "offerings", "title", "updated_at" },
                values: new object[,]
                {
                    { new Guid("c3f2e5a4-0000-4000-8000-000000000002"), "it", "Practical technology services that set up and support the systems your business relies on every day.", 1, new[] { "Financial system setup for payroll and HR", "Website development", "IT infrastructure" }, "IT Services", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("c3f2e5a4-0000-4000-8000-000000000003"), "customer_survey", "Structured customer surveys that capture satisfaction and loyalty so you can act on what your customers value most.", 2, new string[0], "Customer Surveys", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("c3f2e5a4-0000-4000-8000-000000000004"), "employee_survey", "Anonymous employee surveys that surface engagement and workplace sentiment to help you build a stronger team.", 3, new string[0], "Employee Surveys", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "company_content",
                keyColumn: "id",
                keyValue: new Guid("b2e1d4f3-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "company_content",
                keyColumn: "id",
                keyValue: new Guid("b2e1d4f3-0000-4000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "service_blocks",
                keyColumn: "id",
                keyValue: new Guid("c3f2e5a4-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "service_blocks",
                keyColumn: "id",
                keyValue: new Guid("c3f2e5a4-0000-4000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "service_blocks",
                keyColumn: "id",
                keyValue: new Guid("c3f2e5a4-0000-4000-8000-000000000003"));

            migrationBuilder.DeleteData(
                table: "service_blocks",
                keyColumn: "id",
                keyValue: new Guid("c3f2e5a4-0000-4000-8000-000000000004"));
        }
    }
}
