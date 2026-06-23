// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { scanFiles, isBinaryText } from './leak-check.mjs';

const NONE = new Set();

test('clean source file passes', () => {
  assert.deepEqual(scanFiles([{ path: 'src/Foo.cs', content: 'class Foo {}' }], NONE), []);
});

test('forbidden path prefix is blocked', () => {
  const v = scanFiles([{ path: 'docs/notes.md', content: 'hi' }], NONE);
  assert.equal(v.length, 1);
  assert.match(v[0], /forbidden path/);
});

test('forbidden exact file (CLAUDE.md) is blocked', () => {
  assert.equal(scanFiles([{ path: 'CLAUDE.md', content: 'hi' }], NONE).length, 1);
});

test('mbox From envelope outside fixtures is flagged', () => {
  const v = scanFiles([{ path: 'src/x.txt', content: 'From a@b.com Mon Jan 1 10:00:00 2020\nhi' }], NONE);
  assert.ok(v.some((m) => /mbox "From "/.test(m)));
});

test('mbox From inside fixtures with allowlisted email passes', () => {
  const allow = new Set(['@example.com']);
  const content = 'From a@example.com Mon Jan 1 10:00:00 2020\nSubject: hi';
  assert.deepEqual(scanFiles([{ path: 'fixtures/sample.mbox', content }], allow), []);
});

test('non-allowlisted email is flagged', () => {
  const v = scanFiles([{ path: 'src/x.cs', content: 'contact real.person@gmail.com' }], NONE);
  assert.ok(v.some((m) => /email/.test(m)));
});

test('allowlisted email by domain passes; by exact address passes', () => {
  assert.deepEqual(scanFiles([{ path: 'src/x.cs', content: 'a@example.com' }], new Set(['@example.com'])), []);
  assert.deepEqual(scanFiles([{ path: 'README.md', content: 'noreply@github.com' }], new Set(['noreply@github.com'])), []);
});

test('lockfiles are exempt from the email scan', () => {
  assert.deepEqual(scanFiles([{ path: 'desktop/package-lock.json', content: 'x@gmail.com' }], NONE), []);
});

test('home-directory absolute path is flagged', () => {
  assert.ok(scanFiles([{ path: 'src/x.cs', content: 'var p = "C:/Users/bob/mail";' }], NONE)
    .some((m) => /home-directory/.test(m)));
});

test('token-like secret is flagged', () => {
  const tok = 'ghp_' + 'a'.repeat(36);
  assert.ok(scanFiles([{ path: 'src/x.cs', content: `const t = "${tok}";` }], NONE)
    .some((m) => /token/.test(m)));
});

test('.githooks own files are content-exempt (no self-flagging)', () => {
  const content = 'C:/Users/ /home/ ghp_' + 'a'.repeat(36) + ' real@gmail.com From x@y.com Mon Jan 1 2020';
  assert.deepEqual(scanFiles([{ path: '.githooks/leak-check.mjs', content }], NONE), []);
});

test('null content (binary) is skipped', () => {
  assert.deepEqual(scanFiles([{ path: 'assets/logo.ico', content: null }], NONE), []);
});

test('Danish characters (Æ Ø Å) in clean content do not trip the scanner', () => {
  // ASCII-based PII patterns are unaffected by non-ASCII letters; clean Danish prose passes.
  const content = '// Håndtering af mærkelige tegn: Æ Ø Å æ ø å — ingen lækage her';
  assert.deepEqual(scanFiles([{ path: 'src/Doc.cs', content }], NONE), []);
});

test('a real .dk email is flagged (2-letter TLD is caught)', () => {
  const v = scanFiles([{ path: 'src/x.cs', content: 'kontakt@firma.dk' }], NONE);
  assert.ok(v.some((m) => /email/.test(m)));
});

test('isBinaryText: spaces in text are NOT binary', () => {
  assert.equal(isBinaryText('From a@b.com with spaces'), false);
});

test('asset/version filename (128x128@2x.png) is NOT treated as an email', () => {
  const content = '"icons/128x128@2x.png", "deps": "lodash@4.17.21"';
  assert.deepEqual(scanFiles([{ path: 'desktop/src-tauri/tauri.conf.json', content }], NONE), []);
});

test('a real .com email is still flagged (asset-TLD skip does not over-broaden)', () => {
  assert.ok(scanFiles([{ path: 'src/x.cs', content: 'real@gmail.com' }], NONE)
    .some((m) => /email/.test(m)));
});

test('LEAK_CHECK.md is content-exempt (documents the patterns it would otherwise trip)', () => {
  const content = '- home path like C:/Users/bob and an address a@gmail.com';
  assert.deepEqual(scanFiles([{ path: 'LEAK_CHECK.md', content }], NONE), []);
});

test('isBinaryText: a NUL byte marks content binary', () => {
  assert.equal(isBinaryText('abc\u0000def'), true);
});
