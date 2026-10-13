using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AvitoMonitor.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "search_queries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    PriceMin = table.Column<int>(type: "integer", nullable: true),
                    PriceMax = table.Column<int>(type: "integer", nullable: true),
                    SearchId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_queries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "searches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastScanAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastWatchAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WatchEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_searches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "listings",
                columns: table => new
                {
                    SearchId = table.Column<string>(type: "text", nullable: false),
                    AvitoId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    PublishedText = table.Column<string>(type: "text", nullable: true),
                    SellerName = table.Column<string>(type: "text", nullable: true),
                    SellerUrl = table.Column<string>(type: "text", nullable: true),
                    SellerReviews = table.Column<int>(type: "integer", nullable: true),
                    SellerLabel = table.Column<string>(type: "text", nullable: true),
                    Badges = table.Column<string>(type: "text", nullable: true),
                    SourceLabel = table.Column<string>(type: "text", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetailsLoaded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listings", x => new { x.SearchId, x.AvitoId });
                    table.ForeignKey(
                        name: "FK_listings_searches_SearchId",
                        column: x => x.SearchId,
                        principalTable: "searches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SearchId = table.Column<string>(type: "text", nullable: true),
                    SearchName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ListingsCount = table.Column<int>(type: "integer", nullable: false),
                    ExcludedCount = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reports_searches_SearchId",
                        column: x => x.SearchId,
                        principalTable: "searches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "search_sources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SearchId = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    Query = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    PriceMin = table.Column<int>(type: "integer", nullable: true),
                    PriceMax = table.Column<int>(type: "integer", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: false),
                    NextPageToScan = table.Column<int>(type: "integer", nullable: false),
                    ScanCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    TotalCountOnAvito = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_search_sources_searches_SearchId",
                        column: x => x.SearchId,
                        principalTable: "searches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reports_CreatedAt",
                table: "reports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_reports_SearchId",
                table: "reports",
                column: "SearchId");

            migrationBuilder.CreateIndex(
                name: "IX_search_queries_CreatedAt",
                table: "search_queries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_search_queries_Text",
                table: "search_queries",
                column: "Text");

            migrationBuilder.CreateIndex(
                name: "IX_search_sources_SearchId_Position",
                table: "search_sources",
                columns: new[] { "SearchId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "listings");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "search_queries");

            migrationBuilder.DropTable(
                name: "search_sources");

            migrationBuilder.DropTable(
                name: "searches");
        }
    }
}
