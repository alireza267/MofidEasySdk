// EasyTrader order page. Talks to the EasyTrader API straight from the browser, following the
// same rules as the MofidEasySdk .NET client (request bodies, token checks, response handling).
'use strict';

(function () {
  // Refuse to run inside a frame, so another site can't overlay this page and trick clicks.
  if (window.top !== window.self) {
    document.body.textContent = 'This page cannot be embedded.';
    return;
  }

  // ---------- EasyTrader API client ----------
  const API = 'https://api-mts.orbis.easytrader.ir/core/api/v2/';
  const SDK_VERSION = 'web-0.1.0';
  const ORDER_FROM = 1000;
  const TIMEOUT_MS = 30000;
  const EXPIRY_SKEW_MS = 30000;
  const SIDES = { Buy: 0, Sell: 1 };
  const VALIDITY_DAY = 0;

  /** Parses a pasted token. Returns { value, expiresAt } or throws with a readable message. */
  function parseToken(raw) {
    let value = (raw || '').trim();
    if (/^bearer\s+/i.test(value)) value = value.replace(/^bearer\s+/i, '').trim();
    const parts = value.split('.');
    if (parts.length !== 3) throw new Error('The token is not a valid JWT (expected three dot-separated parts).');
    let payload;
    try {
      const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      const padded = base64 + '='.repeat((4 - base64.length % 4) % 4);
      const bytes = Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
      payload = JSON.parse(new TextDecoder().decode(bytes));
    } catch (e) {
      throw new Error('The token is not a valid JWT (its payload could not be read).');
    }
    const expiresAt = typeof payload.exp === 'number' ? new Date(payload.exp * 1000) : null;
    return { value, expiresAt };
  }

  /** Case-insensitive property lookup, like the SDK's parser. */
  function prop(obj, name) {
    if (!obj || typeof obj !== 'object') return undefined;
    const key = Object.keys(obj).find((k) => k.toLowerCase() === name.toLowerCase());
    return key === undefined ? undefined : obj[key];
  }

  function parseOmsError(raw) {
    const error = Array.isArray(raw) ? raw[0] : raw;
    if (!error || typeof error !== 'object') return null;
    const name = String(prop(error, 'name') ?? '');
    const kind = name === 'OmsCustomError' ? 'Custom' : (name.endsWith('Error') ? name.slice(0, -5) : name) || 'Unknown';
    const code = Number(prop(error, 'code'));
    return { kind, name, message: String(prop(error, 'error') ?? ''), code: Number.isFinite(code) ? code : 0 };
  }

  /**
   * Sends one order request. Resolves to
   * { ok, outcome, message, orderId, error } where outcome is one of
   * accepted | rejected | unknownState | blocked | auth | apiError | invalid.
   */
  async function send(operation, method, path, body, targetOrderId) {
    if (!state.token) return { ok: false, outcome: 'auth', message: 'Set your access token first.' };
    if (state.token.expiresAt && state.token.expiresAt.getTime() - EXPIRY_SKEW_MS <= Date.now()) {
      return { ok: false, outcome: 'auth', message: 'Your token has expired. Copy a fresh one from d.easytrader.ir.' };
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
    let response;
    let text;
    try {
      response = await fetch(API + path, {
        method,
        headers: {
          'Authorization': 'Bearer ' + state.token.value,
          'Content-Type': 'application/json',
          'Accept': 'application/json',
          'easy-sdk': SDK_VERSION,
        },
        body: JSON.stringify(body),
        signal: controller.signal,
        credentials: 'omit',
        cache: 'no-store',
        referrerPolicy: 'no-referrer',
      });
      text = await response.text();
    } catch (e) {
      const timedOut = e && e.name === 'AbortError';
      if (!timedOut && state.api === 'blocked') {
        return { ok: false, outcome: 'blocked', message: 'The EasyTrader API does not accept requests from this page yet, so nothing was sent.' };
      }
      const target = targetOrderId ? ' of order ' + targetOrderId : '';
      return {
        ok: false, outcome: 'unknownState', orderId: targetOrderId || null,
        message: 'The order state is unknown: the ' + operation + ' request' + target + ' may or may not have been applied ('
          + (timedOut ? 'no response within ' + TIMEOUT_MS / 1000 + ' s' : 'the connection failed') + '). Check your orders on d.easytrader.ir before retrying.',
      };
    } finally {
      clearTimeout(timer);
    }

    if (response.status === 401 || response.status === 403) {
      return { ok: false, outcome: 'auth', message: 'EasyTrader rejected the token (' + response.status + '). Copy a fresh one from d.easytrader.ir.' };
    }

    let json = null;
    try { json = text ? JSON.parse(text) : null; } catch (e) { /* not JSON */ }
    const isSuccessful = prop(json, 'isSuccessful');
    const id = prop(json, 'id');
    const serverMessage = prop(json, 'message');

    if (isSuccessful === false) {
      const error = parseOmsError(prop(json, 'omsError'));
      const reason = error ? error.name + ' (' + error.code + '): ' + error.message : (serverMessage || 'no reason given');
      return { ok: false, outcome: 'rejected', orderId: id || null, error, message: 'Order rejected: ' + reason };
    }
    if (!response.ok) {
      return { ok: false, outcome: 'apiError', message: 'EasyTrader returned ' + response.status + ': ' + (text || '(empty body)') };
    }
    if (isSuccessful !== true) {
      return { ok: false, outcome: 'apiError', message: 'EasyTrader returned ' + response.status + ' but the body is not an order response: ' + (text || '(empty body)') };
    }
    // Delete responses may leave out the ID; the deleted order is the one we asked for.
    const orderId = id || (operation === 'delete' ? targetOrderId : null);
    if (!orderId) {
      return { ok: false, outcome: 'apiError', message: 'EasyTrader accepted the ' + operation + ' request but returned no order ID: ' + text };
    }
    return { ok: true, outcome: 'accepted', orderId: String(orderId), message: serverMessage || '' };
  }

  const easyTrader = {
    addOrder: (o) => send('add', 'POST', 'order', {
      order: { price: o.price, quantity: o.quantity, side: SIDES[o.side], validityType: VALIDITY_DAY, symbolIsin: o.isin, orderFrom: ORDER_FROM },
    }),
    editOrder: (orderId, o) => send('edit', 'PUT', 'order', {
      modifyOrder: { price: o.price, quantity: o.quantity, validityType: VALIDITY_DAY, symbolIsin: o.isin, orderFrom: ORDER_FROM, parentId: orderId },
    }, orderId),
    deleteOrder: (orderId) => send('delete', 'DELETE', 'delete-order', { orderId, orderFrom: ORDER_FROM }, orderId),

    /**
     * Checks whether the API accepts cross-origin requests from this page. A non-simple request
     * makes the browser send a CORS preflight first; if the preflight is refused, fetch throws.
     */
    async checkAccess() {
      try {
        await fetch(API + 'order', {
          method: 'OPTIONS',
          headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer check', 'easy-sdk': SDK_VERSION },
          credentials: 'omit',
          cache: 'no-store',
          referrerPolicy: 'no-referrer',
        });
        return 'ok';
      } catch (e) {
        return navigator.onLine === false ? 'offline' : 'blocked';
      }
    },
  };

  // ---------- page state ----------
  const $ = (id) => document.getElementById(id);
  const fmt = new Intl.NumberFormat('en-US');
  const state = {
    token: null,          // { value, expiresAt }: in memory only
    api: 'checking',      // checking | ok | blocked | offline
    busy: false,
    editingId: null,
    orders: loadOrders(), // { id, isin, side, price, quantity, status, note }
  };

  function loadOrders() {
    try { return JSON.parse(sessionStorage.getItem('trade-orders') || '[]'); } catch (e) { return []; }
  }

  function saveOrders() {
    try { sessionStorage.setItem('trade-orders', JSON.stringify(state.orders)); } catch (e) { /* storage unavailable */ }
    renderOrders();
  }

  function el(tag, props, ...children) {
    const node = document.createElement(tag);
    Object.entries(props || {}).forEach(([key, value]) => {
      if (value === undefined || value === null || value === false) return;
      if (key === 'class') node.className = value;
      else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
      else node.setAttribute(key, value === true ? '' : value);
    });
    children.flat().forEach((child) => {
      if (child === null || child === undefined || child === false) return;
      node.append(child instanceof Node ? child : document.createTextNode(String(child)));
    });
    return node;
  }

  // ---------- API access banner ----------
  function renderApiStatus() {
    const banner = $('api-status');
    banner.replaceChildren();
    if (state.api === 'checking') {
      banner.className = 'banner';
      banner.append(el('span', null, '…'), el('div', null, el('p', null, 'Checking whether the EasyTrader API accepts requests from this page…')));
    } else if (state.api === 'ok') {
      banner.className = 'banner ok';
      banner.append(el('span', null, '✓'), el('div', null, el('p', null, el('strong', null, 'Connected.'), ' The EasyTrader API accepts requests from this page.')));
    } else if (state.api === 'offline') {
      banner.className = 'banner error';
      banner.append(el('span', null, '✕'), el('div', null, el('p', null, el('strong', null, 'You are offline.'), ' Reconnect and reload the page.')));
    } else {
      banner.className = 'banner error';
      banner.append(el('span', null, '✕'), el('div', null,
        el('p', null, el('strong', null, 'The EasyTrader API does not accept requests from this page yet.'),
          ' It only allows browser requests from d.easytrader.ir (CORS), so orders from here would be blocked before they are sent.'),
        el('p', null, 'Until that changes, use the ', el('a', { href: '../#try' }, 'demo app'), ' or the ',
          el('a', { href: '../#quick-start' }, '.NET SDK'), ', which are not affected.')));
    }
    renderButtons();
  }

  // ---------- token ----------
  function renderToken() {
    const dot = $('token-dot');
    const text = $('token-text');
    $('token-clear').hidden = !state.token;
    if (!state.token) {
      dot.className = 'dot bad';
      text.textContent = 'No token set';
    } else if (state.token.expiresAt) {
      const minutes = Math.round((state.token.expiresAt - Date.now()) / 60000);
      const clock = state.token.expiresAt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      if (minutes <= 0) {
        dot.className = 'dot bad';
        text.textContent = 'Token expired at ' + clock + '. Paste a fresh one.';
      } else {
        dot.className = 'dot ok';
        const left = minutes >= 60 ? Math.floor(minutes / 60) + ' h ' + (minutes % 60) + ' min' : minutes + ' min';
        text.textContent = 'Token set · expires at ' + clock + ' (in ' + left + ')';
      }
    } else {
      dot.className = 'dot ok';
      text.textContent = 'Token set (no expiry claim)';
    }
    renderButtons();
  }

  function saveToken() {
    const input = $('token-input');
    if (!input.value.trim()) { input.focus(); return; }
    try {
      state.token = parseToken(input.value);
      input.value = '';
      input.type = 'password';
      $('token-show').textContent = 'Show';
      log('Token', { ok: true, outcome: 'accepted', message: 'Token set. It stays in this tab only.' });
    } catch (e) {
      log('Token', { ok: false, outcome: 'auth', message: e.message });
    }
    renderToken();
    renderOrders();
  }

  function clearToken() {
    state.token = null;
    log('Token', { ok: true, outcome: 'accepted', message: 'Token forgotten.' });
    renderToken();
    renderOrders();
  }

  // ---------- orders ----------
  function readForm() {
    return {
      isin: $('isin').value.trim().toUpperCase(),
      side: document.querySelector('input[name=side]:checked').value,
      price: Number($('price').value),
      quantity: Number($('quantity').value),
    };
  }

  function validate(form) {
    if (!/^[A-Z0-9]{12}$/.test(form.isin)) return 'ISIN must be 12 letters or digits, for example IRO1TBAN0001.';
    if (!Number.isInteger(form.price) || form.price <= 0) return 'Price must be a whole number above 0.';
    if (!Number.isInteger(form.quantity) || form.quantity <= 0) return 'Quantity must be a whole number above 0.';
    return null;
  }

  function updateTotal() {
    const { price, quantity } = readForm();
    $('order-total').replaceChildren(price > 0 && quantity > 0
      ? el('span', null, 'Total: ', el('strong', null, fmt.format(price * quantity)), ' Rials')
      : 'Total: –');
  }

  async function placeOrder(event) {
    event.preventDefault();
    const form = readForm();
    const problem = validate(form);
    if (problem) { log('Add', { ok: false, outcome: 'invalid', message: problem }); return; }
    const summary = form.side.toUpperCase() + ' ' + fmt.format(form.quantity) + ' × ' + form.isin + ' at ' + fmt.format(form.price) + ' Rials';
    if (!confirm('Place a REAL order?\n\n' + summary)) return;

    await withBusy(async () => {
      const result = await easyTrader.addOrder(form);
      log('Add ' + summary, result);
      const row = { isin: form.isin, side: form.side, price: form.price, quantity: form.quantity };
      if (result.ok) {
        state.orders.unshift({ ...row, id: result.orderId, status: 'live' });
      } else if (result.outcome === 'rejected' && result.orderId) {
        state.orders.unshift({ ...row, id: result.orderId, status: 'rejected', note: result.error ? result.error.kind + ' (' + result.error.code + ')' : 'Rejected' });
      } else if (result.outcome === 'unknownState') {
        state.orders.unshift({ ...row, id: '(unknown)', status: 'unknown', note: 'Check on d.easytrader.ir' });
      }
      saveOrders();
    });
  }

  function trackOrder() {
    const id = $('track-id').value.trim();
    const form = readForm();
    if (!id) { $('track-id').focus(); return; }
    const problem = validate(form);
    if (problem) { log('Track', { ok: false, outcome: 'invalid', message: problem }); return; }
    state.orders.unshift({ id, isin: form.isin, side: form.side, price: form.price, quantity: form.quantity, status: 'live', note: 'Added manually' });
    $('track-id').value = '';
    saveOrders();
    log('Track', { ok: true, outcome: 'accepted', message: 'Order ' + id + ' added to the list.' });
  }

  async function editOrder(order, price, quantity) {
    const summary = order.id + ' → ' + fmt.format(quantity) + ' at ' + fmt.format(price) + ' Rials';
    if (!confirm('Edit this REAL order?\n\n' + summary + '\n\nThe edit replaces the order with a new one.')) return;

    await withBusy(async () => {
      const result = await easyTrader.editOrder(order.id, { isin: order.isin, price, quantity });
      log('Edit ' + summary, result);
      if (result.ok) {
        // The old order leaves the matching engine; the new one carries a new ID.
        order.status = 'replaced';
        order.note = 'Replaced by ' + result.orderId;
        state.orders.unshift({ id: result.orderId, isin: order.isin, side: order.side, price, quantity, status: 'live', note: 'Edited from ' + order.id });
      } else if (result.outcome === 'unknownState') {
        order.status = 'unknown';
        order.note = 'Edit state unknown. Check on d.easytrader.ir';
      }
      state.editingId = null;
      saveOrders();
    });
  }

  async function deleteOrder(order) {
    if (!confirm('Delete this order?\n\n' + order.id)) return;
    await withBusy(async () => {
      const result = await easyTrader.deleteOrder(order.id);
      log('Delete ' + order.id, result);
      if (result.ok) {
        order.status = 'deleted';
        order.note = null;
      } else if (result.outcome === 'unknownState') {
        order.status = 'unknown';
        order.note = 'Delete state unknown. Check on d.easytrader.ir';
      }
      saveOrders();
    });
  }

  async function withBusy(work) {
    state.busy = true;
    renderOrders();
    try { await work(); } finally { state.busy = false; renderOrders(); }
  }

  // ---------- rendering ----------
  const statusLabels = { live: 'Live', replaced: 'Replaced', deleted: 'Deleted', rejected: 'Rejected', unknown: 'Unknown' };

  function canSend() {
    return !state.busy && !!state.token && state.api !== 'blocked' && state.api !== 'offline';
  }

  function renderOrders() {
    const body = $('orders-body');
    body.replaceChildren();
    $('orders-empty').hidden = state.orders.length > 0;

    state.orders.forEach((order) => {
      const active = order.status === 'live' || order.status === 'unknown';
      const canAct = active && order.id !== '(unknown)';
      const editing = state.editingId === order.id;
      body.append(el('tr', { class: active ? null : 'inactive' },
        el('td', { class: 'mono' }, order.id),
        el('td', { class: 'mono' }, order.isin),
        el('td', { class: order.side === 'Buy' ? 'side-buy' : 'side-sell' }, order.side),
        el('td', { class: 'num' }, fmt.format(order.price)),
        el('td', { class: 'num' }, fmt.format(order.quantity)),
        el('td', null,
          el('span', { class: 'chip ' + order.status }, statusLabels[order.status] || order.status),
          order.note ? el('div', { class: 'note tight' }, order.note) : null),
        el('td', { class: 'actions' },
          canAct ? el('button', {
            class: 'small', type: 'button', disabled: !canSend(),
            onclick: () => { state.editingId = editing ? null : order.id; renderOrders(); },
          }, editing ? 'Close' : 'Edit') : null,
          canAct ? el('button', { class: 'small danger', type: 'button', disabled: !canSend(), onclick: () => deleteOrder(order) }, 'Delete') : null)));

      if (editing) {
        const priceInput = el('input', { type: 'number', min: '1', step: '1', value: order.price, id: 'edit-price' });
        const quantityInput = el('input', { type: 'number', min: '1', step: '1', value: order.quantity, id: 'edit-quantity' });
        body.append(el('tr', { class: 'edit-row' },
          el('td', { colspan: '7' },
            el('div', { class: 'edit-form' },
              el('div', null, el('label', { for: 'edit-price' }, 'New price'), priceInput),
              el('div', null, el('label', { for: 'edit-quantity' }, 'New quantity'), quantityInput),
              el('button', {
                class: 'primary', type: 'button', disabled: !canSend(),
                onclick: () => {
                  const price = Number(priceInput.value);
                  const quantity = Number(quantityInput.value);
                  if (!Number.isInteger(price) || price <= 0 || !Number.isInteger(quantity) || quantity <= 0) {
                    log('Edit', { ok: false, outcome: 'invalid', message: 'Price and quantity must be whole numbers above 0.' });
                    return;
                  }
                  editOrder(order, price, quantity);
                },
              }, 'Save edit'),
              el('span', { class: 'note flat' }, 'Creates a new order with a new ID.')))));
      }
    });
    renderButtons();
  }

  function renderButtons() {
    const submit = $('order-submit');
    submit.disabled = !canSend();
    submit.textContent = state.busy ? 'Sending…'
      : state.api === 'blocked' ? 'Blocked by the API (see above)'
      : !state.token ? 'Set a token first'
      : 'Place order';
  }

  const outcomeLabels = {
    accepted: ['Accepted', 'live'],
    rejected: ['Rejected', 'rejected'],
    unknownState: ['State unknown', 'unknown'],
    blocked: ['Not sent', 'rejected'],
    auth: ['Token problem', 'rejected'],
    apiError: ['API error', 'rejected'],
    invalid: ['Invalid input', 'rejected'],
  };

  function log(action, result) {
    const [label, cls] = outcomeLabels[result.outcome] || [result.outcome, ''];
    const details = [];
    if (result.orderId) details.push('Order ID: ' + result.orderId);
    if (result.error) details.push(result.error.kind + ' · ' + result.error.name + ' · code ' + result.error.code);
    const text = result.error && result.error.message ? result.error.message : result.message;

    $('log').prepend(el('li', null,
      el('div', { class: 'meta' },
        el('time', null, new Date().toLocaleTimeString()),
        el('span', { class: 'chip ' + cls }, label),
        el('strong', null, action)),
      text ? el('div', { class: 'msg', dir: 'auto' }, text) : null,
      details.length ? el('div', { class: 'detail mono' }, details.join('  |  ')) : null));
    $('log-empty').hidden = true;
  }

  // ---------- wiring ----------
  $('token-save').addEventListener('click', saveToken);
  $('token-input').addEventListener('keydown', (e) => { if (e.key === 'Enter') saveToken(); });
  $('token-clear').addEventListener('click', clearToken);
  $('token-show').addEventListener('click', () => {
    const input = $('token-input');
    input.type = input.type === 'password' ? 'text' : 'password';
    $('token-show').textContent = input.type === 'password' ? 'Show' : 'Hide';
  });
  $('order-form').addEventListener('submit', placeOrder);
  $('track-submit').addEventListener('click', trackOrder);
  ['price', 'quantity'].forEach((id) => $(id).addEventListener('input', updateTotal));
  $('isin').addEventListener('input', (e) => { e.target.value = e.target.value.toUpperCase(); });
  $('log-clear').addEventListener('click', () => { $('log').replaceChildren(); $('log-empty').hidden = false; });

  (function theme() {
    const root = document.documentElement;
    try {
      const saved = localStorage.getItem('theme');
      if (saved === 'light' || saved === 'dark') root.setAttribute('data-theme', saved);
    } catch (e) { /* storage unavailable */ }
    $('theme-toggle').addEventListener('click', () => {
      const current = root.getAttribute('data-theme') || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
      const next = current === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-theme', next);
      try { localStorage.setItem('theme', next); } catch (e) { /* storage unavailable */ }
    });
  })();

  renderToken();
  renderOrders();
  renderApiStatus();
  easyTrader.checkAccess().then((result) => { state.api = result; renderApiStatus(); renderOrders(); });
  setInterval(renderToken, 30000);
})();
