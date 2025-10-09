// Minimal REST client for Dense11 (numeric actions 0/1/2).

export const OBS_TYPE = 'Dense11';
export const ACTIONS = ['Straight', 'TurnRight', 'TurnLeft'];

export default class SnakeRestDense11 {
  constructor(baseUrl = 'http://localhost:8080/v1') {
    this.base = baseUrl.replace(/\/$/, '');
  }

  static actionIndexToName(i) {
    if (i < 0 || i >= ACTIONS.length) throw new RangeError(`action index out of range: ${i}`);
    return ACTIONS[i];
  }
  static actionNameToIndex(name) {
    const idx = ACTIONS.indexOf(String(name).trim());
    if (idx === -1) throw new Error(`unknown action '${name}'. valid: ${ACTIONS.join(', ')}`);
    return idx;
  }

  async spec() {
    const res = await fetch(`${this.base}/spec`);
    if (!res.ok) throw new Error(`spec() HTTP ${res.status}`);
    return res.json();
  }

  async reset(seed) {
    const res = await fetch(`${this.base}/reset`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ seed: Number(seed), obs_type: OBS_TYPE }),
    });
    if (!res.ok) throw new Error(`reset() HTTP ${res.status}`);
    const data = await res.json();
    const dense = data?.obs?.dense;
    if (!dense) throw new Error(`Expected Dense11 payload on reset; got type=${data?.obs?.type}`);
    const arr = Float32Array.from(dense.data ?? []);
    if (arr.length !== 11) throw new Error(`Dense11 expected length 11, got ${arr.length}`);
    return arr;
  }

  async step(action) {
    // send numeric int (0/1/2)
    const idx = typeof action === 'number' ? action : SnakeRestDense11.actionNameToIndex(action);
    if (idx < 0 || idx > 2) throw new Error('action must be 0,1,2');

    const res = await fetch(`${this.base}/step`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ action: idx }),
    });
    if (!res.ok) throw new Error(`step() HTTP ${res.status}`);
    const data = await res.json();

    const dense = data?.obs?.dense;
    if (!dense) throw new Error(`Expected Dense11 payload on step; got type=${data?.obs?.type}`);
    const vec = Float32Array.from(dense.data ?? []);
    if (vec.length !== 11) throw new Error(`Dense11 expected length 11, got ${vec.length}`);

    return {
      obs: vec,
      signals: Array.isArray(data?.signals) ? data.signals.map(Number) : [0,0,0,0,0,0],
      done: Boolean(data?.done),
      score: Number(data?.score ?? 0),
      length: Number(data?.length ?? 0),
      death: String(data?.death ?? ''),
      steps: Number(data?.steps ?? 0),
      raw: data,
    };
  }
}
