// sarvaghna/core/gemini-client.test.mjs
// DISC-14: the shared request-count quota check/record in callGeminiJson, run against an isolated
// temp ledger file (SARVAGHNA_QUOTA_LEDGER_PATH) — never the real, shared
// sarvaghna/ledger/gemini-requests.jsonl, and never a real network call.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'fs';
import os from 'os';
import path from 'path';
import { callGeminiJson } from './gemini-client.mjs';

function tempLedgerPath() {
  return path.join(os.tmpdir(), `sarvaghna-quota-test-${process.pid}-${Math.random().toString(36).slice(2)}.jsonl`);
}

function writeTodayEntries(ledgerPath, count) {
  const nowUtc = new Date().toISOString();
  const lines = Array.from({ length: count }, () => JSON.stringify({ timestampUtc: nowUtc, source: 'test', capability: 'test' }));
  fs.mkdirSync(path.dirname(ledgerPath), { recursive: true });
  fs.writeFileSync(ledgerPath, lines.join('\n') + '\n');
}

test('missing GEMINI_API_KEY is refused before the quota is ever checked', async () => {
  const previousKey = process.env.GEMINI_API_KEY;
  delete process.env.GEMINI_API_KEY;
  try {
    const result = await callGeminiJson({ prompt: 'anything' });
    assert.equal(result.ok, false);
    assert.match(result.reason, /GEMINI_API_KEY not configured/);
  } finally {
    if (previousKey !== undefined) process.env.GEMINI_API_KEY = previousKey;
  }
});

test('at the daily request quota, the call is refused and the ledger is not touched again', async (t) => {
  const ledgerPath = tempLedgerPath();
  writeTodayEntries(ledgerPath, 3);
  const sizeBeforeCall = fs.statSync(ledgerPath).size;

  const previousKey = process.env.GEMINI_API_KEY;
  const previousLedger = process.env.SARVAGHNA_QUOTA_LEDGER_PATH;
  process.env.GEMINI_API_KEY = 'test-key-not-real';
  process.env.SARVAGHNA_QUOTA_LEDGER_PATH = ledgerPath;

  // Any real network call here would be a test bug — prove it by failing loudly if one happens.
  const fetchMock = t.mock.method(globalThis, 'fetch', async () => {
    throw new Error('fetch should never be called once the quota is already exhausted');
  });

  try {
    const result = await callGeminiJson({ prompt: 'anything', dailyRequestQuota: 3 });

    assert.equal(result.ok, false);
    assert.match(result.reason, /AI request quota reached: 3 of 3/);
    assert.equal(fetchMock.mock.callCount(), 0);
    assert.equal(fs.statSync(ledgerPath).size, sizeBeforeCall); // refused calls never record a line
  } finally {
    if (previousKey !== undefined) process.env.GEMINI_API_KEY = previousKey; else delete process.env.GEMINI_API_KEY;
    if (previousLedger !== undefined) process.env.SARVAGHNA_QUOTA_LEDGER_PATH = previousLedger; else delete process.env.SARVAGHNA_QUOTA_LEDGER_PATH;
    fs.rmSync(ledgerPath, { force: true });
  }
});

test('under quota, a real response is recorded to the ledger regardless of its status code', async (t) => {
  const ledgerPath = tempLedgerPath();

  const previousKey = process.env.GEMINI_API_KEY;
  const previousLedger = process.env.SARVAGHNA_QUOTA_LEDGER_PATH;
  process.env.GEMINI_API_KEY = 'test-key-not-real';
  process.env.SARVAGHNA_QUOTA_LEDGER_PATH = ledgerPath;

  t.mock.method(globalThis, 'fetch', async () => ({
    ok: false,
    status: 429,
    text: async () => '{"error":{"message":"simulated rate limit"}}'
  }));

  try {
    const result = await callGeminiJson({ prompt: 'anything', dailyRequestQuota: 20, capability: 'unit-test' });

    assert.equal(result.ok, false); // the simulated 429 itself still fails the call...
    const lines = fs.readFileSync(ledgerPath, 'utf8').trim().split('\n');
    assert.equal(lines.length, 1); // ...but it still reached Gemini, so it still counts.
    const entry = JSON.parse(lines[0]);
    assert.equal(entry.source, 'sarvaghna-node');
    assert.equal(entry.capability, 'unit-test');
  } finally {
    if (previousKey !== undefined) process.env.GEMINI_API_KEY = previousKey; else delete process.env.GEMINI_API_KEY;
    if (previousLedger !== undefined) process.env.SARVAGHNA_QUOTA_LEDGER_PATH = previousLedger; else delete process.env.SARVAGHNA_QUOTA_LEDGER_PATH;
    fs.rmSync(ledgerPath, { force: true });
  }
});
