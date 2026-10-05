import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  maskConnectionString,
  validateMigrationSql,
  checkExpandContractCompatibility,
  applyLocalDevMigrations,
} from '../migration-ops.mjs';
import { validateSqlMigration } from '../blue-green/migration-check.mjs';

describe('Migration Operations & Safety Tests', () => {
  describe('maskConnectionString', () => {
    it('masks password in PostgreSQL URI', () => {
      const uri = 'postgresql://db_user:super_secret_password@db.internal:5432/restaurant_order';
      const masked = maskConnectionString(uri);
      assert.strictEqual(masked, 'postgresql://db_user:***@db.internal:5432/restaurant_order');
      assert.doesNotMatch(masked, /super_secret_password/);
    });

    it('masks password in ADO.NET connection string', () => {
      const conn = 'Host=db.internal;Port=5432;Database=restaurant_order;Username=app_user;Password=my_secret_pass;';
      const masked = maskConnectionString(conn);
      assert.strictEqual(masked, 'Host=db.internal;Port=5432;Database=restaurant_order;Username=app_user;Password=***;');
      assert.doesNotMatch(masked, /my_secret_pass/);
    });

    it('masks Pwd in short ADO.NET connection string', () => {
      const conn = 'Server=db;Database=test;User Id=usr;Pwd=secret;';
      const masked = maskConnectionString(conn);
      assert.strictEqual(masked, 'Server=db;Database=test;User Id=usr;Pwd=***;');
      assert.doesNotMatch(masked, /secret/);
    });

    it('returns empty string for null/empty input', () => {
      assert.strictEqual(maskConnectionString(''), '');
      assert.strictEqual(maskConnectionString(null), '');
    });
  });

  describe('validateMigrationSql & Destructive Pattern Detection', () => {
    it('detects DROP TABLE as destructive', () => {
      const sql = 'DROP TABLE tenants;';
      const violations = validateMigrationSql(sql);
      assert.strictEqual(violations.length, 1);
      assert.match(violations[0].description, /DROP TABLE/);
    });

    it('detects DROP COLUMN as destructive', () => {
      const sql = 'ALTER TABLE tenants DROP COLUMN name;';
      const violations = validateMigrationSql(sql);
      assert.ok(violations.length >= 1);
      assert.ok(violations.some(v => v.description.includes('DROP COLUMN') || v.description.includes('DROP')));
    });

    it('detects TRUNCATE as destructive', () => {
      const sql = 'TRUNCATE TABLE branches;';
      const violations = validateMigrationSql(sql);
      assert.strictEqual(violations.length, 1);
      assert.match(violations[0].description, /TRUNCATE/);
    });

    it('detects RENAME COLUMN as destructive', () => {
      const sql = 'ALTER TABLE brands RENAME COLUMN slug TO brand_slug;';
      const violations = validateMigrationSql(sql);
      assert.strictEqual(violations.length, 1);
      assert.match(violations[0].description, /RENAME COLUMN/);
    });

    it('detects ADD COLUMN NOT NULL without DEFAULT', () => {
      const sql = 'ALTER TABLE tenants ADD COLUMN tax_id text NOT NULL;';
      const violations = validateMigrationSql(sql);
      assert.strictEqual(violations.length, 1);
      assert.match(violations[0].description, /ADD COLUMN NOT NULL without DEFAULT/);
    });

    it('permits ADD COLUMN NOT NULL with DEFAULT', () => {
      const sql = "ALTER TABLE tenants ADD COLUMN is_active boolean NOT NULL DEFAULT true;";
      const violations = validateMigrationSql(sql);
      assert.strictEqual(violations.length, 0);
    });

    it('permits safe additive schema operations', () => {
      const sql = `
        CREATE SCHEMA IF NOT EXISTS tenancy;
        CREATE TABLE IF NOT EXISTS tenancy.tenants (
          id uuid PRIMARY KEY,
          name text NOT NULL,
          created_at timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ix_tenants_slug ON tenancy.tenants (slug);
        ALTER TABLE tenancy.tenants ADD COLUMN description text NULL;
      `;
      const violations = validateMigrationSql(sql);
      assert.strictEqual(violations.length, 0);
    });

    it('rejects DROP PRIMARY KEY constraint', () => {
      const sql = 'ALTER TABLE orders DROP CONSTRAINT pk_orders;';
      const violations = validateMigrationSql(sql);
      assert.ok(violations.some(v => v.description.includes('DROP PRIMARY KEY/CONSTRAINT is strictly prohibited')));
    });

    it('rejects DROP UNIQUE CONSTRAINT when not allowlisted', () => {
      const sql = 'ALTER TABLE users DROP CONSTRAINT uq_users_email;';
      const violations = validateMigrationSql(sql);
      assert.ok(violations.some(v => v.description.includes('DROP UNIQUE CONSTRAINT is strictly prohibited')));
    });

    it('rejects DROP CHECK CONSTRAINT', () => {
      const sql = 'ALTER TABLE products DROP CONSTRAINT ck_price_positive;';
      const violations = validateMigrationSql(sql);
      assert.ok(violations.some(v => v.description.includes('DROP CHECK CONSTRAINT is strictly prohibited')));
    });

    it('rejects unallowlisted foreign key drop', () => {
      const sql = 'ALTER TABLE orders DROP CONSTRAINT fk_orders_random_ref;';
      const violations = validateMigrationSql(sql);
      assert.ok(violations.some(v => v.description.includes("prohibited before cutover (unauthorized constraint drop)")));
    });

    it('rejects allowlisted foreign key drop when replacement is missing', () => {
      const sql = 'ALTER TABLE branch_item_availabilities DROP CONSTRAINT fk_branch_item_availabilities_menu_items_item_id;';
      const violations = validateMigrationSql(sql);
      assert.ok(violations.some(v => v.description.includes("requires replacement 'fk_branch_item_availabilities_menu_items_tenant_branch_item'")));
    });

    it('permits allowlisted foreign key drop when replacement constraint is present in same migration', () => {
      const sql = `
        ALTER TABLE branch_item_availabilities DROP CONSTRAINT fk_branch_item_availabilities_menu_items_item_id;
        ALTER TABLE branch_item_availabilities ADD CONSTRAINT fk_branch_item_availabilities_menu_items_tenant_branch_item FOREIGN KEY (tenant_id, branch_id, item_id) REFERENCES menu_items (tenant_id, branch_id, id);
      `;
      const violations = validateMigrationSql(sql);
      assert.strictEqual(violations.length, 0);
    });
  });

  describe('C# Migration Constraint Replacement & False-Positive Guardrails', () => {
    const validators = [
      { name: 'validateMigrationSql', fn: (code) => validateMigrationSql(code, { isCSharp: true }).map(v => v.description) },
      { name: 'validateSqlMigration', fn: (code) => validateSqlMigration(code, { isCSharp: true }) },
    ];

    for (const { name, fn } of validators) {
      describe(`${name} C# guardrails`, () => {
        it('rejects same-name allowlisted DropForeignKey when Add is missing (Drop-only false-positive fix)', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "menu_items");
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.length > 0, 'Must produce violation for drop without add');
          assert.ok(violations.some(v => v.includes("requires replacement 'fk_menu_items_prep_stations_tenant_branch_station'")));
        });

        it('accepts same-name allowlisted DropForeignKey with genuine AddForeignKey on matching table', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "menu_items");

                migrationBuilder.AddForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "menu_items",
                    columns: new[] { "tenant_id", "branch_id", "preparation_station_id" },
                    principalSchema: "tenancy",
                    principalTable: "preparation_stations",
                    principalColumns: new[] { "tenant_id", "branch_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            }
          `;
          const violations = fn(cs);
          assert.strictEqual(violations.length, 0, 'Genuine Drop + Add must be accepted');
        });

        it('rejects replacement when name appears only in comment', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "menu_items");
                // migrationBuilder.AddForeignKey(name: "fk_menu_items_prep_stations_tenant_branch_station", table: "menu_items");
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.length > 0);
          assert.ok(violations.some(v => v.includes("requires replacement 'fk_menu_items_prep_stations_tenant_branch_station'")));
        });

        it('rejects replacement when name appears only in string literal', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "menu_items");
                var message = "migrationBuilder.AddForeignKey(name: \\"fk_menu_items_prep_stations_tenant_branch_station\\", table: \\"menu_items\\")";
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.length > 0);
          assert.ok(violations.some(v => v.includes("requires replacement 'fk_menu_items_prep_stations_tenant_branch_station'")));
        });

        it('rejects replacement when Add exists only in Down method', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "menu_items");
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "menu_items",
                    columns: new[] { "tenant_id", "branch_id", "preparation_station_id" });
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.length > 0);
          assert.ok(violations.some(v => v.includes("requires replacement 'fk_menu_items_prep_stations_tenant_branch_station'")));
        });

        it('rejects replacement when AddForeignKey is applied to a different table', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "menu_items");

                migrationBuilder.AddForeignKey(
                    name: "fk_menu_items_prep_stations_tenant_branch_station",
                    schema: "tenancy",
                    table: "orders");
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.length > 0);
          assert.ok(violations.some(v => v.includes("requires replacement 'fk_menu_items_prep_stations_tenant_branch_station'")));
        });

        it('accepts different-named allowlisted Drop + correct Add', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_menu_items_menus_tenant_id_menu_id",
                    schema: "tenancy",
                    table: "menu_items");

                migrationBuilder.AddForeignKey(
                    name: "fk_menu_items_menus_tenant_branch_menu",
                    schema: "tenancy",
                    table: "menu_items");
            }
          `;
          const violations = fn(cs);
          assert.strictEqual(violations.length, 0);
        });

        it('rejects different-named allowlisted Drop with wrong Add constraint name', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_menu_items_menus_tenant_id_menu_id",
                    schema: "tenancy",
                    table: "menu_items");

                migrationBuilder.AddForeignKey(
                    name: "fk_menu_items_wrong_replacement",
                    schema: "tenancy",
                    table: "menu_items");
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.length > 0);
          assert.ok(violations.some(v => v.includes("requires replacement 'fk_menu_items_menus_tenant_branch_menu'")));
        });

        it('rejects unallowlisted foreign key drop in C#', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "fk_unallowlisted_ref",
                    schema: "tenancy",
                    table: "orders");
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.length > 0);
          assert.ok(violations.some(v => v.includes("prohibited before cutover (unauthorized constraint drop)")));
        });

        it('rejects Primary Key drop in C#', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropPrimaryKey(
                    name: "pk_menu_items",
                    schema: "tenancy",
                    table: "menu_items");
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.some(v => v.includes("DROP PRIMARY KEY/CONSTRAINT is strictly prohibited")));
        });

        it('rejects Check constraint drop in C#', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropCheckConstraint(
                    name: "ck_items_price",
                    schema: "tenancy",
                    table: "menu_items");
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.some(v => v.includes("DROP CHECK CONSTRAINT is strictly prohibited")));
        });

        it('rejects unauthorized Unique constraint drop in C#', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropUniqueConstraint(
                    name: "uq_arbitrary_field",
                    schema: "tenancy",
                    table: "menu_items");
            }
          `;
          const violations = fn(cs);
          assert.ok(violations.some(v => v.includes("DROP UNIQUE CONSTRAINT is strictly prohibited")));
        });

        it('accepts historical allowlisted unique constraint AK_menus_tenant_id_id with replacement', () => {
          const cs = `
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropUniqueConstraint(
                    name: "AK_menus_tenant_id_id",
                    schema: "tenancy",
                    table: "menus");

                migrationBuilder.AddUniqueConstraint(
                    name: "AK_menus_tenant_id_branch_id_id",
                    schema: "tenancy",
                    table: "menus",
                    columns: new[] { "tenant_id", "branch_id", "id" });
            }
          `;
          const violations = fn(cs);
          assert.strictEqual(violations.length, 0);
        });
      });
    }
  });

  describe('checkExpandContractCompatibility', () => {
    it('reports compatible for additive schema changes', () => {
      const sql = 'ALTER TABLE tenancy.brands ADD COLUMN display_order int NULL;';
      const result = checkExpandContractCompatibility(sql);
      assert.strictEqual(result.isCompatible, true);
      assert.strictEqual(result.violations.length, 0);
    });

    it('reports incompatible for destructive schema changes', () => {
      const sql = 'DROP TABLE tenancy.branches;';
      const result = checkExpandContractCompatibility(sql);
      assert.strictEqual(result.isCompatible, false);
      assert.strictEqual(result.violations.length, 1);
    });
  });

  describe('applyLocalDevMigrations Environment Guardrails', () => {
    it('throws error when invoked in Production environment', () => {
      const origEnv = process.env.ASPNETCORE_ENVIRONMENT;
      try {
        process.env.ASPNETCORE_ENVIRONMENT = 'Production';
        assert.throws(() => {
          applyLocalDevMigrations();
        }, /FATAL.*Production/);
      } finally {
        process.env.ASPNETCORE_ENVIRONMENT = origEnv;
      }
    });

    it('throws error when invoked in Staging environment', () => {
      const origEnv = process.env.ASPNETCORE_ENVIRONMENT;
      try {
        process.env.ASPNETCORE_ENVIRONMENT = 'Staging';
        assert.throws(() => {
          applyLocalDevMigrations();
        }, /FATAL.*Staging/);
      } finally {
        process.env.ASPNETCORE_ENVIRONMENT = origEnv;
      }
    });
  });
});
