'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { Pool } = require('pg');

function createPool(databaseUrl) {
  // Render's internal database URL needs no TLS; its external one (and most other hosts) does.
  const local = /@(localhost|127\.0\.0\.1|\[::1\]|[^.@/:]+)(:\d+)?\//.test(databaseUrl);
  return new Pool({
    connectionString: databaseUrl,
    ssl: local ? undefined : { rejectUnauthorized: false },
    max: 10,
  });
}

// Runs each .sql file in src/migrations once, in name order.
async function migrate(pool) {
  const dir = path.join(__dirname, 'migrations');
  const client = await pool.connect();
  try {
    await client.query('SELECT pg_advisory_lock(727001)');
    await client.query(
      'CREATE TABLE IF NOT EXISTS schema_migrations (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())'
    );
    const done = new Set((await client.query('SELECT name FROM schema_migrations')).rows.map((r) => r.name));
    for (const name of fs.readdirSync(dir).filter((f) => f.endsWith('.sql')).sort()) {
      if (done.has(name)) continue;
      const sql = fs.readFileSync(path.join(dir, name), 'utf8');
      await client.query('BEGIN');
      try {
        await client.query(sql);
        await client.query('INSERT INTO schema_migrations (name) VALUES ($1)', [name]);
        await client.query('COMMIT');
      } catch (err) {
        await client.query('ROLLBACK');
        throw new Error(`Migration ${name} failed: ${err.message}`);
      }
    }
  } finally {
    await client.query('SELECT pg_advisory_unlock(727001)').catch(() => {});
    client.release();
  }
}

module.exports = { createPool, migrate };
