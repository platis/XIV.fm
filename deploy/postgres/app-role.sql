-- Creates or updates the limited role the XIV.fm API uses. Run as the database owner:
--   psql --set ON_ERROR_STOP=1 --set app_password=... --file app-role.sql
-- The role can read and change rows, but cannot change the schema, roles, or other databases.
-- Migrations keep using the owner.
\set QUIET on

select 'create role xivfm_app'
where not exists (select from pg_roles where rolname = 'xivfm_app')
\gexec

alter role xivfm_app with
    login nosuperuser nocreatedb nocreaterole noreplication nobypassrls
    password :'app_password';

revoke all on database :"DBNAME" from public;
grant connect on database :"DBNAME" to xivfm_app;
revoke create on schema public from public;
grant usage on schema public to xivfm_app;

grant select, insert, update, delete on all tables in schema public to xivfm_app;
alter default privileges in schema public
    grant select, insert, update, delete on tables to xivfm_app;

-- Migration history is read-only for the API.
do $$
begin
    if to_regclass('public."__EFMigrationsHistory"') is not null then
        revoke all on table public."__EFMigrationsHistory" from xivfm_app;
        grant select on table public."__EFMigrationsHistory" to xivfm_app;
    end if;
end
$$;
