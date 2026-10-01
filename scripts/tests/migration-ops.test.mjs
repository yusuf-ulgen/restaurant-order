import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  maskConnectionString,
  validateMigrationSql,
  checkExpandContractCompatibility,
  applyLocalDevMigrations,
} from '../migration-ops.mjs';

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
