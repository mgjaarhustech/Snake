/**
 * clients/js/snakeRestDense11.mjs
 * ------------------------------------------------------------
 * Minimal REST client for the Snake environment using Dense11.
 *
 * What this provides:
 *   - spec()             → GET /v1/spec
 *   - reset(seed)        → POST /v1/reset   (returns Dense11 vector)
 *   - step(action)       → POST /v1/step    (returns Dense11, signals, done, etc.)
 *   - ACTIONS/OBS_TYPE   → constants (REST expects PascalCase)
 *   - Helpers to map action index <-> name
 *
 * What this does NOT include:
 *   - NO DQN / replay / training / evaluation logic
 *   - NO reward shaping (students decide how to use signals)
 *
 * Requirements:
 *   - Node.js 18+ (global fetch)
 *   - If on Node <18, install: npm i node-fetch && import('node-fetch').then(...)
 */

export const OBS_TYPE = 'Dense11';                      // REST expects PascalCase obs type
export const ACTIONS = ['Straight', 'TurnRight', 'TurnLeft']; // discrete action names in order 0..2

/**
 * @typedef {Object} StepResult
 * @property {Float32Array} obs     - Dense11 vector (length 11)
 * @property {number[]}     signals - [eat_food, death, step_cost, toward_food, turning, timeout]
 * @property {boolean}      done
 * @property {number}       score   - apples eaten
 * @property {number}       length  - snake length
 * @property {string}       death   - "", "wall", "self", "timeout"
 * @property {number}       steps   - total steps elapsed in env
 * @property {Object}       raw     - full JSON response (for debugging)
 */

export default class SnakeRestDense11 {
  /**
   * @param {string} [baseUrl='http://localhost:8080/v1'] - REST base URL (no trailing slash)
   */
  constructor(baseUrl = 'http://localhost:8080/v1') {
    this.base = baseUrl.replace(/\/$/, '');
  }

  // -----------------------
  // Helpers
  // -----------------------

  /** @param {number} i - 0..2 */
  static actionIndexToName(i) {
    if (i < 0 || i >= ACTIONS.length) {
      throw new RangeError(`action index out of range: ${i} (valid 0..${ACTIONS.length - 1})`);
    }
    return ACTIONS[i];
  }

  /** @param {string} name - "Straight" | "TurnRight" | "TurnLeft" */
  static actionNameToIndex(name) {
    const idx = ACTIONS.indexOf(String(name).trim());
    if (idx === -1) throw new Error(`unknown action '${name}'. valid: ${ACTIONS.join(', ')}`);
    return idx;
  }

  // -----------------------
  // Endpoints
  // -----------------------

  /**
   * GET /v1/spec → environment description
   * @returns {Promise<Object>}
   */
  async spec() {
    const res = await fetch(`${this.base}/spec`, { method: 'GET' });
    if (!res.ok) throw new Error(`spec() HTTP ${res.status}`);
    return await res.json();
  }

  /**
   * POST /v1/reset → returns Dense11 vector for the initial observation
   * @param {number} seed - 64-bit unsigned value fits in JS number range for typical use
   * @returns {Promise<Float32Array>} Dense11 (length 11)
   */
  async reset(seed) {
    const body = { seed: Number(seed), obs_type: OBS_TYPE };
    const res = await fetch(`${this.base}/reset`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!res.ok) throw new Error(`reset() HTTP ${res.status}`);
    const data = await res.json();

    const obs = data?.obs ?? {};
    const dense = obs?.dense;
    if (!dense) {
      throw new Error(
        `Expected Dense11 payload on reset; got keys=${Object.keys(obs)} type=${obs?.type}`
      );
    }

    const arr = Float32Array.from(dense.data ?? []);
    if (arr.length !== 11) {
      throw new Error(`Dense11 expected length 11, got ${arr.length}`);
    }
    return arr;
  }

  /**
   * POST /v1/step → returns next Dense11 observation and transition info
   * @param {number|string} action - 0..2 or one of ACTIONS
   * @returns {Promise<StepResult>}
   */
  async step(action) {
    const actionName =
      typeof action === 'number' ? SnakeRestDense11.actionIndexToName(action) : String(action);

    const res = await fetch(`${this.base}/step`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ action: actionName }),
    });
    if (!res.ok) throw new Error(`step() HTTP ${res.status}`);
    const data = await res.json();

    // Observation (Dense11)
    const obs = data?.obs ?? {};
    const dense = obs?.dense;
    if (!dense) {
      throw new Error(
        `Expected Dense11 payload on step; got keys=${Object.keys(obs)} type=${obs?.type}`
      );
    }
    const vec = Float32Array.from(dense.data ?? []);
    if (vec.length !== 11) {
      throw new Error(`Dense11 expected length 11, got ${vec.length}`);
    }

    /** @type {number[]} */
    const signals = Array.isArray(data?.signals) ? data.signals.map(Number) : [0, 0, 0, 0, 0, 0];

    /** @type {StepResult} */
    const result = {
      obs: vec,
      signals,
      done: Boolean(data?.done),
      score: Number(data?.score ?? 0),
      length: Number(data?.length ?? 0),
      death: String(data?.death ?? ''),
      steps: Number(data?.steps ?? 0),
      raw: data,
    };

    return result;
  }
}

/* ------------------------------------------------------------------
   Minimal demo (NOT learning) — safe for you to delete.
   Run with: node clients/js/snakeRestDense11.mjs
------------------------------------------------------------------- */
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const THIS_FILE = fileURLToPath(import.meta.url);
if (process.argv[1] && resolve(process.argv[1]) === resolve(THIS_FILE)) {
  (async () => {
    const env = new SnakeRestDense11('http://localhost:8080/v1');

    try {
      console.log('SPEC:', await env.spec());

      // Deterministic reset
      let s = await env.reset(123n); // bigint ok; Number(123n) works above
      console.log('Reset Dense11 length:', s.length);

      // Take 10 random steps (no reward shaping, no learning)
      for (let t = 0; t < 10; t++) {
        const a = Math.floor(Math.random() * ACTIONS.length);
        const r = await env.step(a);
        console.log(
          `t=${String(t).padStart(2, '0')}  a=${ACTIONS[a].padStart(9)}  len=${String(r.length).padStart(2, ' ')}  score=${r.score}  done=${r.done}  death='${r.death}'  steps=${r.steps}`
        );
        if (r.done) break;
        s = r.obs;
      }
      console.log('Done.');
    } catch (err) {
      console.error('Demo failed:', err);
      process.exitCode = 1;
    }
  })();
}
