using FluentMigrator;

namespace EMQ.Server.Db.Migrations.Auth;

[Tags("AUTH")]
[Migration(20261011_000)]
public class AddResendCountToVerificationForgottenPassword : Migration
{
    private const string TableName = "verification_forgottenpassword";

    public override void Up()
    {
        Alter.Table(TableName).AddColumn("resend_count").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Column("resend_count").FromTable(TableName);
    }
}
