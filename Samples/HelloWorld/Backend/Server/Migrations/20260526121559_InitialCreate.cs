using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Target = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    SourceIpAddress = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    SourceCountryIsoCode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    CompressedPayload = table.Column<byte[]>(type: "longblob", nullable: false),
                    CompressionAlgorithm = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "AuthEntries",
                columns: table => new
                {
                    AuthKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    HashedAuthToken = table.Column<string>(type: "varchar(160)", nullable: true),
                    PlayerId = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthEntries", x => x.AuthKey);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseScanCoordinators",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseScanCoordinators", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseScanWorkers",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseScanWorkers", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectorKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", nullable: false),
                    ServerName = table.Column<string>(type: "varchar(64)", nullable: false),
                    KeyBytes = table.Column<byte[]>(type: "longblob", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "DateTime", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "DateTime", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "DateTime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectorKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalStates",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalStates", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "InAppPurchases",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Event = table.Column<byte[]>(type: "longblob", nullable: false),
                    IsValidReceipt = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<string>(type: "varchar(64)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InAppPurchases", x => x.TransactionId);
                });

            migrationBuilder.CreateTable(
                name: "InAppPurchaseSubscriptions",
                columns: table => new
                {
                    PlayerAndOriginalTransactionId = table.Column<string>(type: "varchar(530)", maxLength: 530, nullable: false),
                    PlayerId = table.Column<string>(type: "varchar(64)", nullable: false),
                    OriginalTransactionId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    SubscriptionInfo = table.Column<byte[]>(type: "longblob", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InAppPurchaseSubscriptions", x => x.PlayerAndOriginalTransactionId);
                });

            migrationBuilder.CreateTable(
                name: "KeyManagers",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyManagers", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "LiveOpsTimelineManagers",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveOpsTimelineManagers", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "MetaInfo",
                columns: table => new
                {
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "DateTime", nullable: false),
                    MasterVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    NumShards = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaInfo", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "PlayerDeletionRecords",
                columns: table => new
                {
                    PlayerId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScheduledDeletionAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    DeletionSource = table.Column<string>(type: "varchar(128)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDeletionRecords", x => x.PlayerId);
                });

            migrationBuilder.CreateTable(
                name: "PlayerEventLogSegments",
                columns: table => new
                {
                    GlobalId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SegmentSequentialId = table.Column<int>(type: "INTEGER", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    FirstEntryTimestamp = table.Column<DateTime>(type: "DateTime", nullable: false),
                    LastEntryTimestamp = table.Column<DateTime>(type: "DateTime", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerEventLogSegments", x => x.GlobalId);
                });

            migrationBuilder.CreateTable(
                name: "PlayerIncidents",
                columns: table => new
                {
                    IncidentId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PlayerId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    SubType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    Compression = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerIncidents", x => x.IncidentId);
                });

            migrationBuilder.CreateTable(
                name: "PlayerNameSearches",
                columns: table => new
                {
                    Priority = table.Column<uint>(type: "tinyint", nullable: false, defaultValue: 64u),
                    NamePart = table.Column<string>(type: "varchar(32)", nullable: false),
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: true),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false),
                    LogicVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    LifecycleStatus = table.Column<sbyte>(type: "tinyint", nullable: true),
                    DeletionSource = table.Column<sbyte>(type: "tinyint", nullable: true),
                    ScheduledForDeletionAt = table.Column<DateTime>(type: "DateTime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "SegmentEstimates",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentEstimates", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "ServerDrivenInAppPurchases",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    PlayerId = table.Column<string>(type: "varchar(64)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    PurchasePlatform = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    PurchasePlatformUserId = table.Column<string>(type: "varchar(512)", nullable: false),
                    ProductId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerDrivenInAppPurchases", x => x.TransactionId);
                });

            migrationBuilder.CreateTable(
                name: "StaticGameConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    MetaDataBytes = table.Column<byte[]>(type: "longblob", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    VersionHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Source = table.Column<string>(type: "varchar(128)", nullable: false),
                    ArchiveBuiltAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "DateTime", nullable: true),
                    UnpublishedAt = table.Column<DateTime>(type: "DateTime", nullable: true),
                    IsArchived = table.Column<bool>(type: "tinyint", nullable: false),
                    TaskId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    FailureInfo = table.Column<string>(type: "LONGTEXT", nullable: true),
                    ArchiveBytes = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaticGameConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatisticsEvents",
                columns: table => new
                {
                    UniqueKey = table.Column<string>(type: "varchar(128)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "DateTime(3)", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatisticsEvents", x => x.UniqueKey);
                });

            migrationBuilder.CreateTable(
                name: "StatisticsPages",
                columns: table => new
                {
                    UniqueKey = table.Column<string>(type: "varchar(128)", nullable: false),
                    ResolutionName = table.Column<string>(type: "varchar(128)", nullable: false),
                    StartTime = table.Column<DateTime>(type: "DateTime", nullable: false),
                    EndTime = table.Column<DateTime>(type: "DateTime", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatisticsPages", x => x.UniqueKey);
                });

            migrationBuilder.CreateTable(
                name: "StatsCollectors",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatsCollectors", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "SteamworksPollers",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PersistedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SteamworksPollers", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "WebLoginAuthorizations",
                columns: table => new
                {
                    AuthorizationCode = table.Column<string>(type: "char(32)", nullable: false),
                    ClientId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    RedirectUri = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    S256CodeChallenge = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    State = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    Scope = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    FlowExpiresAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    CodeExchangeExpiresAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    UserInfo = table.Column<byte[]>(type: "longblob", nullable: true),
                    LoginMethod = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebLoginAuthorizations", x => x.AuthorizationCode);
                });

            migrationBuilder.CreateTable(
                name: "WebLoginClientSessions",
                columns: table => new
                {
                    ClientSessionId = table.Column<string>(type: "char(32)", nullable: false),
                    RefreshTokenNonce = table.Column<string>(type: "char(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    ClientId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Scope = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "DateTime", nullable: false),
                    LoginMethod = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    UserInfo = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebLoginClientSessions", x => x.ClientSessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEvents_Source",
                table: "AuditLogEvents",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEvents_Target",
                table: "AuditLogEvents",
                column: "Target");

            migrationBuilder.CreateIndex(
                name: "IX_DataProtectorKeys_ExpiresAt",
                table: "DataProtectorKeys",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_InAppPurchaseSubscriptions_OriginalTransactionId",
                table: "InAppPurchaseSubscriptions",
                column: "OriginalTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InAppPurchaseSubscriptions_PlayerId",
                table: "InAppPurchaseSubscriptions",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEventLogSegments_OwnerId",
                table: "PlayerEventLogSegments",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerIncidents_Fingerprint_PersistedAt",
                table: "PlayerIncidents",
                columns: new[] { "Fingerprint", "PersistedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerIncidents_PersistedAt",
                table: "PlayerIncidents",
                column: "PersistedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerIncidents_PlayerId",
                table: "PlayerIncidents",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerNameSearches_EntityId",
                table: "PlayerNameSearches",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerNameSearches_NamePart_EntityId",
                table: "PlayerNameSearches",
                columns: new[] { "NamePart", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerNameSearches_Priority_NamePart_EntityId",
                table: "PlayerNameSearches",
                columns: new[] { "Priority", "NamePart", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerDrivenInAppPurchases_PlayerId",
                table: "ServerDrivenInAppPurchases",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerDrivenInAppPurchases_PurchasePlatform_PurchasePlatformUserId_ProductId",
                table: "ServerDrivenInAppPurchases",
                columns: new[] { "PurchasePlatform", "PurchasePlatformUserId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_StatisticsEvents_Timestamp",
                table: "StatisticsEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_StatisticsPages_CreatedAt",
                table: "StatisticsPages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StatisticsPages_EndTime",
                table: "StatisticsPages",
                column: "EndTime");

            migrationBuilder.CreateIndex(
                name: "IX_StatisticsPages_StartTime",
                table: "StatisticsPages",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_WebLoginAuthorizations_FlowExpiresAt",
                table: "WebLoginAuthorizations",
                column: "FlowExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebLoginClientSessions_ExpiresAt",
                table: "WebLoginClientSessions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogEvents");

            migrationBuilder.DropTable(
                name: "AuthEntries");

            migrationBuilder.DropTable(
                name: "DatabaseScanCoordinators");

            migrationBuilder.DropTable(
                name: "DatabaseScanWorkers");

            migrationBuilder.DropTable(
                name: "DataProtectorKeys");

            migrationBuilder.DropTable(
                name: "GlobalStates");

            migrationBuilder.DropTable(
                name: "InAppPurchases");

            migrationBuilder.DropTable(
                name: "InAppPurchaseSubscriptions");

            migrationBuilder.DropTable(
                name: "KeyManagers");

            migrationBuilder.DropTable(
                name: "LiveOpsTimelineManagers");

            migrationBuilder.DropTable(
                name: "MetaInfo");

            migrationBuilder.DropTable(
                name: "PlayerDeletionRecords");

            migrationBuilder.DropTable(
                name: "PlayerEventLogSegments");

            migrationBuilder.DropTable(
                name: "PlayerIncidents");

            migrationBuilder.DropTable(
                name: "PlayerNameSearches");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "SegmentEstimates");

            migrationBuilder.DropTable(
                name: "ServerDrivenInAppPurchases");

            migrationBuilder.DropTable(
                name: "StaticGameConfigs");

            migrationBuilder.DropTable(
                name: "StatisticsEvents");

            migrationBuilder.DropTable(
                name: "StatisticsPages");

            migrationBuilder.DropTable(
                name: "StatsCollectors");

            migrationBuilder.DropTable(
                name: "SteamworksPollers");

            migrationBuilder.DropTable(
                name: "WebLoginAuthorizations");

            migrationBuilder.DropTable(
                name: "WebLoginClientSessions");
        }
    }
}
