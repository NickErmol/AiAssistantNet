using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIHelperNET.Integration.Tests.Persistence.UpgradeFixture;

[DbContext(typeof(MigrationUpgradeTestContext))]
[Migration("20000101000001_UpgradeV2")]
public sealed class UpgradeV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "Note", table: "Widgets", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "Note", table: "Widgets");
}
