using System;
using FluentMigrator;

namespace EMQ.Server.Db.Migrations.Auth;

[Tags("AUTH")]
[Migration(20261010_000)]
public class GrantSerecolaPermissions : Migration
{
    public override void Up()
    {
        // Grant the following permissions to the Serecola user by adding them to inc_perm
        // (IncludedPermissions), which AuthStuff.HasPermission unions into the effective permission set:
        // SetAvatar(3009), NekobakoUpload(3100), Donor(3200), ChangeUsernameColor(3201),
        // ChangeUsernameAnimation(3202), UseUploadedImageAvatar(3203), ImportHelper(3700),
        // Moderator(4000), ModerateChat(5001), ReviewSongLink(6001), DeleteSongLink(6002),
        // ReviewEdit(6003), Delete(7001), EditUser(8001), Admin(9000)
        Execute.Sql(
            "UPDATE users SET inc_perm = '{3009,3100,3200,3201,3202,3203,3700,4000,5001,6001,6002,6003,7001,8001,9000}'::integer[] " +
            "WHERE lower(username) = lower('Serecola')");
    }

    public override void Down() => throw new NotImplementedException();
}
