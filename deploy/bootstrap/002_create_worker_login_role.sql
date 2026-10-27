-- Ayrı worker kimliği. DBA bu betiği migration öncesi kasa enjeksiyonuyla çalıştırır.
-- Parola komut satırına, Git'e veya günlüğe yazılmaz.
\set ON_ERROR_STOP on
\getenv worker_runtime_password WORKER_RUNTIME_PASSWORD
\if :{?worker_runtime_password}
SELECT CASE WHEN length(:'worker_runtime_password') = 0 THEN
  'DO $$ BEGIN RAISE EXCEPTION ''WORKER_RUNTIME_PASSWORD boş olamaz.''; END $$;'
ELSE format($bootstrap$
  DO $body$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_worker_runtime') THEN
      CREATE ROLE restaurant_worker_runtime NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'restaurant_worker_user') THEN
      EXECUTE format('CREATE ROLE restaurant_worker_user LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD %%L', %L);
    ELSE
      EXECUTE format('ALTER ROLE restaurant_worker_user PASSWORD %%L', %L);
    END IF;
    GRANT restaurant_worker_runtime TO restaurant_worker_user;
  END $body$;
$bootstrap$, :'worker_runtime_password', :'worker_runtime_password') END \gexec
\else
DO $$ BEGIN RAISE EXCEPTION 'WORKER_RUNTIME_PASSWORD gerekli.'; END $$;
\endif
