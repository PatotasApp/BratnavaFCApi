using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAdjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Store = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    ReferencePriceCents = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Store = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PurchaseToken = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    PriceMicros = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPurchases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    EnforcementEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RemindersEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderDays = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    LaunchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingTrialGrants",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingTrialGrants", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "GroupBillings",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrialStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TrialEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TrialGranted = table.Column<bool>(type: "boolean", nullable: false),
                    PaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HasPaidPeriod = table.Column<bool>(type: "boolean", nullable: false),
                    IsLifetime = table.Column<bool>(type: "boolean", nullable: false),
                    LifetimeReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LastReminderKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupBillings", x => x.GroupId);
                    table.ForeignKey(
                        name: "FK_GroupBillings_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Store = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NotificationType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Subtype = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoreReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAdjustments_GroupId_CreateDate",
                table: "BillingAdjustments",
                columns: new[] { "GroupId", "CreateDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingProducts_Store_Months",
                table: "BillingProducts",
                columns: new[] { "Store", "Months" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_BillingProducts_Store_ProductId",
                table: "BillingProducts",
                columns: new[] { "Store", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPurchases_GroupId_PurchasedAt",
                table: "BillingPurchases",
                columns: new[] { "GroupId", "PurchasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPurchases_PurchaseToken",
                table: "BillingPurchases",
                column: "PurchaseToken");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPurchases_Store_TransactionId",
                table: "BillingPurchases",
                columns: new[] { "Store", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingTrialGrants_GroupId",
                table: "BillingTrialGrants",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreEvents_GroupId",
                table: "StoreEvents",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreEvents_Status_CreateDate",
                table: "StoreEvents",
                columns: new[] { "Status", "CreateDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreEvents_Store_ExternalId",
                table: "StoreEvents",
                columns: new[] { "Store", "ExternalId" },
                unique: true);

            // Parâmetros iniciais: 30 dias de teste, 3 de tolerância, bloqueio e lembretes
            // DESLIGADOS. LaunchedAt = agora: patotas que já existem ganham o teste a partir
            // do primeiro acesso, não da criação.
            migrationBuilder.Sql("""
                INSERT INTO "BillingSettings" ("Id", "TrialDays", "GraceDays", "EnforcementEnabled", "RemindersEnabled", "ReminderDays", "LaunchedAt")
                VALUES (1, 30, 3, FALSE, FALSE, '7,3,1,0', now())
                ON CONFLICT ("Id") DO NOTHING;
                """);

            // Pacotes avulsos (Store: 1 = Apple, 2 = Google, 3 = Simulada). Preço de referência
            // em centavos: R$ 29,90/mês; 3, 6 e 12 meses com 5%, 7% e 10% de desconto.
            // O preço real é o cadastrado em cada loja.
            migrationBuilder.Sql("""
                INSERT INTO "BillingProducts" ("Id", "Store", "ProductId", "Months", "ReferencePriceCents", "IsActive", "CreateDate")
                SELECT gen_random_uuid(), s.store, p.product_id, p.months, p.cents, TRUE, now()
                FROM (VALUES (1), (2), (3)) AS s(store)
                CROSS JOIN (VALUES
                    ('patota_1m', 1, 2990),
                    ('patota_3m', 3, 8490),
                    ('patota_6m', 6, 16690),
                    ('patota_12m', 12, 32290)
                ) AS p(product_id, months, cents)
                ON CONFLICT DO NOTHING;
                """);

            // Bratnava FC: plano vitalício (isenção permanente). Só se a patota existir no banco.
            migrationBuilder.Sql("""
                INSERT INTO "GroupBillings" ("GroupId", "TrialStartedAt", "TrialEndsAt", "TrialGranted", "HasPaidPeriod", "IsLifetime", "LifetimeReason", "CreatedAt")
                SELECT g."Id", now(), now(), FALSE, FALSE, TRUE, 'Bratnava FC: plano vitalício', now()
                FROM "Groups" g
                WHERE g."Id" = '3f401edf-d309-4bae-97d8-28eae0da7a8a'
                ON CONFLICT ("GroupId") DO UPDATE SET "IsLifetime" = TRUE, "LifetimeReason" = EXCLUDED."LifetimeReason";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingAdjustments");

            migrationBuilder.DropTable(
                name: "BillingProducts");

            migrationBuilder.DropTable(
                name: "BillingPurchases");

            migrationBuilder.DropTable(
                name: "BillingSettings");

            migrationBuilder.DropTable(
                name: "BillingTrialGrants");

            migrationBuilder.DropTable(
                name: "GroupBillings");

            migrationBuilder.DropTable(
                name: "StoreEvents");
        }
    }
}
