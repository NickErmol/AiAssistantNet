using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIHelperNET.Integration.Tests.Persistence.UpgradeFixture;

[DbContext(typeof(MigrationUpgradeTestContext))]
[Migration("20000101000000_UpgradeV1")]
public sealed class UpgradeV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateTable(
            name: "Widgets",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false),
                Name = table.Column<string>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Widgets", x => x.Id));

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable("Widgets");
}
