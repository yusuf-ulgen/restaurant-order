CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'tenancy') THEN
            CREATE SCHEMA tenancy;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE TABLE tenancy.tenants (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        slug character varying(64) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_tenants" PRIMARY KEY (id),
        CONSTRAINT ck_tenants_status CHECK (status IN ('Active', 'Suspended', 'Closed'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE TABLE tenancy.brands (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name character varying(200) NOT NULL,
        slug character varying(64) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_brands" PRIMARY KEY (id),
        CONSTRAINT ak_brands_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_brands_status CHECK (status IN ('Active', 'Inactive')),
        CONSTRAINT fk_brands_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE TABLE tenancy.branches (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        brand_id uuid NOT NULL,
        name character varying(200) NOT NULL,
        slug character varying(64) NOT NULL,
        timezone character varying(50) NOT NULL,
        currency character varying(3) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        concurrency_token uuid NOT NULL,
        CONSTRAINT "PK_branches" PRIMARY KEY (id),
        CONSTRAINT ck_branches_status CHECK (status IN ('Active', 'Suspended', 'Closed')),
        CONSTRAINT fk_branches_brands_tenant_id_brand_id FOREIGN KEY (tenant_id, brand_id) REFERENCES tenancy.brands (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_branches_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE INDEX ix_branches_tenant_id_brand_id ON tenancy.branches (tenant_id, brand_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE UNIQUE INDEX ix_branches_tenant_id_slug ON tenancy.branches (tenant_id, slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE UNIQUE INDEX ix_brands_tenant_id_slug ON tenancy.brands (tenant_id, slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    CREATE UNIQUE INDEX ix_tenants_slug ON tenancy.tenants (slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920181655_InitialTenancySchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260920181655_InitialTenancySchema', '10.0.4');
    END IF;
END $EF$;
COMMIT;

