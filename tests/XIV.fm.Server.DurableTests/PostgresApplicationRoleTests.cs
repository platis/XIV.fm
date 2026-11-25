using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace XIV.fm.Server.DurableTests;

[Collection(DurableStoresGroup.Name)]
public sealed class PostgresApplicationRoleTests
{
    private readonly DurableStores stores;

    public PostgresApplicationRoleTests(DurableStores stores)
    {
        this.stores = stores;
    }

    [Fact]
    public async Task StoresUseTheLimitedRole()
    {
        await using var context = await this.stores.Contexts.CreateDbContextAsync();
        var isLimited = await context.Database
            .SqlQueryRaw<bool>(
                "select current_user = 'xivfm_app' and not rolsuper and not rolcreatedb and not rolcreaterole and not rolbypassrls as \"Value\" from pg_roles where rolname = current_user")
            .SingleAsync();
        Assert.True(isLimited);
    }

    [Theory]
    [InlineData("create table intruder (id int)")]
    [InlineData("alter table lastfm_accounts add column intruder int")]
    [InlineData("drop table lastfm_accounts")]
    [InlineData("truncate lastfm_accounts cascade")]
    [InlineData("create role intruder")]
    [InlineData("create database intruder")]
    [InlineData("alter role xivfm_app with superuser")]
    [InlineData("select rolpassword from pg_authid")]
    [InlineData("copy (select 1) to program 'true'")]
    public async Task AdministrativeStatementsAreDenied(string statement)
    {
        await using var context = await this.stores.Contexts.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = statement;

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
