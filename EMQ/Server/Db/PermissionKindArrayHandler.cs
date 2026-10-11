using System;
using System.Data;
using System.Linq;
using Dapper;
using EMQ.Shared.Auth.Entities.Concrete;

namespace EMQ.Server.Db;

public class PermissionKindArrayHandler : SqlMapper.TypeHandler<PermissionKind[]>
{
    public override void SetValue(IDbDataParameter parameter, PermissionKind[]? value)
    {
        parameter.Value = value is null
            ? DBNull.Value
            : value.Select(x => (int)x).ToArray();
    }

    public override PermissionKind[] Parse(object value)
    {
        return ((int[])value).Select(x => (PermissionKind)x).ToArray();
    }
}