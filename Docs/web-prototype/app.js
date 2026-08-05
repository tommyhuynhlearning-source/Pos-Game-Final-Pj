'use strict';

/* ===================== DATA (mirrors the ScriptableObject schema) ===================== */

const STORE = {
  storeId: 'ST-1042',
  storeName: 'Sunrise Diner',
  ownerName: 'Maria Alvarez',
  machineId: 'REG-1',
  phone: '555-0142',
  remoteId: '482 913 706', // fixed device ID, like a TeamViewer/AnyDesk ID
};

// CRM search directory — includes decoys with similar names/different addresses so a fuzzy search by
// store ID or name can return MULTIPLE hits. The player must pick the record that actually matches this
// caller — verified via the click-to-compare mechanic (see compareStatusHtml/handleCompareClick below),
// not an auto-revealed ground truth. Every record shows ITS OWN remote credentials — picking the wrong
// one and trying to connect just fails, teaching verification without ever hard-blocking the player the
// way a strict exact-match search did.
const STORE_DIRECTORY = [
  { storeId: STORE.storeId, storeName: STORE.storeName, ownerName: STORE.ownerName, machineId: STORE.machineId, address: '482 Elm St', remoteId: STORE.remoteId, isReal: true },
  { storeId: 'ST-2071', storeName: 'Sunrise Bakery', ownerName: 'Tom Reyes', machineId: 'REG-4', address: '19 Oak Ave', remoteId: '551 204 918', fixedPasscode: 'QX7K2' },
  { storeId: 'ST-3390', storeName: 'Sunset Diner', ownerName: 'Priya Nair', machineId: 'REG-9', address: '77 Birch Rd', remoteId: '390 447 216', fixedPasscode: 'M4RTZ' },
];

function searchCrmDirectory(query) {
  const q = query.trim().toLowerCase();
  if (!q) return [];
  return STORE_DIRECTORY.filter(r => r.storeId.toLowerCase().includes(q) || r.storeName.toLowerCase().includes(q));
}

// Mirrors PersonaProfileSO (GDD Mục 5) field-for-field, so this prototype's data shape ports directly
// into Unity later. misnaming/laymanVocabulary aren't wired into any generator here — chat lines are
// hand-written, not LLM-produced — they exist so the schema itself is already correct on the JS side.
const PERSONA = {
  personaId: 'sunrise-diner-default',
  displayName: 'Sunrise Diner caller',
  techLiteracy: 0.3,
  cooperativeness: 0.6,
  memoryAccuracy: 0.5,
  emotionalState: 0.4,
  honesty: 0.6,
  misnaming: [
    { correctTerm: 'POS software', customerTerm: 'the till' },
    { correctTerm: 'terminal', customerTerm: 'the card machine' },
    { correctTerm: 'receipt printer', customerTerm: 'the printer thingy' },
    { correctTerm: 'network', customerTerm: 'the internet thingy' },
  ],
  laymanVocabulary: ['the till', 'the card machine', 'the printer thingy', 'the internet thingy', "won't ring anything up"],
};

// On refund/void-case tickets the caller is genuinely a DIFFERENT person than the owner (a staff member
// calling on their behalf) — otherwise "Ask if owner authorized this" is nonsense (the owner doesn't need
// the owner's permission to be themselves). Every other ticket, the owner is just calling in directly.
const STAFF_CALLER_NAMES = ['Jenny Park', 'Carlos Ibarra', 'Deshawn Miller'];

// The real, correct DB server address — POS Manager ▸ Connections lets the player edit
// desktop.POSSoftware.dbHost directly; it only connects when this matches (GDD nguyên tắc #3:
// "Lỗi = state sai trong module" — fix = đưa state về đúng, not a boolean toggle).
const POS_DB_HOST_CORRECT = 'db.sunrisediner.local';

// The store's real Wi-Fi network.
const STORE_WIFI_SSID = 'SunriseDiner-Main';

// Nearby networks a real Wi-Fi picker would show — each with its OWN subnet (IP/gateway), just like real
// DHCP: joining a network doesn't let you pick your own IP, the network hands you one from its own range.
// Terminal ▸ Network only lets the player choose the SSID; IP/gateway are always DERIVED from that choice.
const WIFI_NETWORKS = {
  [STORE_WIFI_SSID]: { ip: '192.168.1.50', gateway: '192.168.1.1' },
  'SunriseDiner-Guest': { ip: '192.168.50.23', gateway: '192.168.50.1' },
  'CoffeeShop-Public': { ip: '10.10.10.87', gateway: '10.10.10.1' },
  'iPhone-Hotspot': { ip: '172.20.10.4', gateway: '172.20.10.1' },
};
const NEARBY_WIFI_NETWORKS = Object.keys(WIFI_NETWORKS);

// The correct IP for the store's own network — what POS Manager ▸ Connections should have registered.
const TERMINAL_IP_CORRECT = WIFI_NETWORKS[STORE_WIFI_SSID].ip;

// What the terminal's network settings ACTUALLY resolve to right now (DHCP-derived, never independently
// editable) — used everywhere instead of reading desktop.Terminal fields directly.
function terminalNetInfo(desktop) {
  return WIFI_NETWORKS[desktop.Terminal.wifiNetwork] || { ip: 'unassigned', gateway: 'unassigned' };
}

const ACTIONS = [
  { id: 'check_print_queue', kind: 'Diagnostic', target: 'Printer', app: 'printer', label: 'Check print queue' },
  { id: 'print_test_page', kind: 'Diagnostic', target: 'Printer', app: 'printer', label: 'Print test page', isTest: true, testType: 'TestPage' },
  // Triggered from POS Manager ▸ Database (printReceiptFor), since POS Software owns the receipt
  // template + DB connection — per GDD, Customer/Merchant/Store receipts need real transaction data.
  { id: 'print_customer_copy', kind: 'Diagnostic', target: 'Printer', label: 'Print customer copy', isTest: true, testType: 'CustomerCopy' },
  { id: 'refill_paper_tray', kind: 'Fix', target: 'Printer', app: 'printer', label: 'Refill paper tray',
    pre: [{ field: 'paperLevel', op: 'Equals', value: 'Empty' }], changes: { paperLevel: 'OK' } },

  { id: 'open_device_manager', kind: 'Diagnostic', target: 'Printer', app: 'devicemanager', label: 'Open Device Manager' },
  { id: 'reinstall_printer_driver', kind: 'Fix', target: 'Printer', app: 'devicemanager', label: 'Reinstall printer driver',
    pre: [{ field: 'driverState', op: 'Equals', value: 'Corrupted' }], changes: { driverState: 'OK' } },
  { id: 'remove_readd_printer', kind: 'Fix', target: 'Printer', app: 'devicemanager', label: 'Remove & re-add printer device (risky)',
    risky: true, changes: { connection: 'Removed' },
    riskyWarning: "This can fully disconnect the printer if the driver wasn't actually the real cause. Continue?" },

  { id: 'check_port_config', kind: 'Diagnostic', target: 'CashDrawer', app: 'cashdrawer', label: 'Check port config' },
  { id: 'move_cash_drawer_port', kind: 'Fix', target: 'CashDrawer', app: 'cashdrawer', label: 'Move cash drawer to COM4',
    pre: [{ field: 'port', op: 'Equals', value: 'COM3' }], changes: { port: 'COM4' } },

  { id: 'check_network_status', kind: 'Diagnostic', target: 'Network', app: 'network', label: 'Check network status' },
  { id: 'reconnect_network', kind: 'Fix', target: 'Network', app: 'network', label: 'Reconnect network',
    pre: [{ field: 'isOnline', op: 'Equals', value: false }], changes: { isOnline: true } },

  { id: 'check_pos_receipt_config', kind: 'Diagnostic', target: 'POSSoftware', app: 'possoftware', label: 'Check POS receipt config' },
  { id: 'reset_pos_receipt_template', kind: 'Fix', target: 'POSSoftware', app: 'possoftware', label: 'Reset POS receipt template',
    pre: [{ field: 'receiptTemplate', op: 'Equals', value: 'Broken' }], changes: { receiptTemplate: 'OK' } },

  // P6/P7 clue source — reveals both the terminal's Wi-Fi and IP the moment Terminal ▸ Network is opened.
  // Fixes themselves happen via the real Wi-Fi picker / IP field / POS registration field (wired directly
  // in terminalNetworkTab / posManagerConnectionsTab), not through this generic action button.
  { id: 'check_terminal_network', kind: 'Diagnostic', target: 'Terminal', app: 'terminal', label: 'Check terminal network config' },
];

const APP_DEFS = {
  possoftware: { title: 'POS Manager', targetModule: 'POSSoftware', icon: '🧾' },
  printer: { title: 'Printer & Print Queue', targetModule: 'Printer', icon: '🖨' },
  devicemanager: { title: 'Device Manager', targetModule: 'Printer', icon: '🛠' },
  network: { title: 'Network Settings', targetModule: 'Network', icon: '📶' },
  cashdrawer: { title: 'Cash Drawer Config', targetModule: 'CashDrawer', icon: '💵' },
  terminal: { title: 'POS Terminal', targetModule: 'Terminal', icon: '💳' },
};

// category mirrors IssueSO.category (IssueCategory enum, GDD Mục 5: Terminal/POS/Printer/OS/Network/
// Business — extended here with CashDrawer since this prototype models it as its own module).
// symptoms mirrors IssueSO.symptoms: Symptom[] — layman is all the customer ever says (used in chat),
// technical is the GDD's "CHỈ hiện khi remote — customer KHÔNG có" line, not surfaced to the customer.
const ISSUES = {
  P1: {
    id: 'P1', title: 'Receipt printer prints nothing', category: 'Printer', tier: 'Basic',
    symptoms: [{ layman: "The receipt printer just won't print anything, and the paper tray light is on.", technical: 'Printer.paperLevel = Empty.' }],
    faultModule: 'Printer', faults: { paperLevel: 'Empty' },
    clues: [
      { actionId: 'check_print_queue', text: "Print queue shows: 'Out of paper'.", redHerring: false },
      { actionId: 'check_print_queue', text: 'Toner light is blinking (looks unrelated to this job).', redHerring: true },
    ],
    resolution: { rootCause: [{ module: 'Printer', field: 'paperLevel', op: 'Equals', value: 'OK' }], requiresTestPass: true, testType: 'TestPage' },
  },
  P2: {
    id: 'P2', title: 'Printer driver error', category: 'Printer', tier: 'Medium',
    symptoms: [{ layman: "The printer is jammed or something — nothing comes out and there's a red light.", technical: 'Printer.driverState = Corrupted (Device Manager Code 39).' }],
    faultModule: 'Printer', faults: { driverState: 'Corrupted' },
    clues: [
      { actionId: 'open_device_manager', text: "Device Manager: 'This device cannot start (Code 39)'. Print queue looks stuck.", redHerring: false },
    ],
    resolution: {
      rootCause: [{ module: 'Printer', field: 'driverState', op: 'Equals', value: 'OK' }, { module: 'Printer', field: 'connection', op: 'NotEquals', value: 'Removed' }],
      requiresTestPass: true, testType: 'TestPage',
    },
  },
  P3: {
    id: 'P3', title: 'Cash drawer stopped opening', category: 'CashDrawer', tier: 'Hard',
    symptoms: [{ layman: "Weird — now the cash drawer doesn't pop open automatically anymore.", technical: 'CashDrawer.port conflicts with Printer.port (COM3).' }],
    faultModule: 'CashDrawer', faults: { port: 'COM3' },
    clues: [
      { actionId: 'check_port_config', text: 'Cash Drawer is set to COM3 — same port as the Printer. Printer driver status looks OK though.', redHerring: false },
    ],
    resolution: { rootCause: [{ module: 'CashDrawer', field: 'port', op: 'NotEquals', value: 'COM3' }], requiresTestPass: true, testType: 'TestPage' },
  },
  P4: {
    id: 'P4', title: 'Whole front-of-house looks dead', category: 'Network', tier: 'Hard', isBlocker: true,
    symptoms: [{ layman: "Everything at the front feels dead — the internet thingy shows no bars, and the printer's not working either.", technical: 'Network.isOnline = false — gateway unreachable.' }],
    faultModule: 'Network', faults: { isOnline: false },
    clues: [
      { actionId: 'check_network_status', text: 'Ping to gateway: Request timed out. Network adapter shows Disconnected.', redHerring: false },
    ],
    resolution: { rootCause: [{ module: 'Network', field: 'isOnline', op: 'Equals', value: true }], requiresTestPass: false },
  },
  P5: {
    id: 'P5', title: "Customer copy missing info", category: 'POS', tier: 'Hard',
    symptoms: [{ layman: "The test print looks fine, but the customer's copy is missing stuff, like the total is cut off.", technical: 'POSSoftware.receiptTemplate = Broken — customer-copy field mapping corrupted.' }],
    faultModule: 'POSSoftware', faults: { receiptTemplate: 'Broken' },
    clues: [
      { actionId: 'print_test_page', text: 'Test page prints perfectly — hardware and driver look fine.', redHerring: false },
      { actionId: 'print_customer_copy', text: 'Customer copy prints, but the total field is cut off.', redHerring: false },
      { actionId: 'check_pos_receipt_config', text: 'POS receipt template config shows a corrupted field mapping (missing total field).', redHerring: false },
    ],
    resolution: { rootCause: [{ module: 'POSSoftware', field: 'receiptTemplate', op: 'Equals', value: 'OK' }], requiresTestPass: true, testType: 'CustomerCopy' },
  },
  P6: {
    id: 'P6', title: 'Terminal joined the wrong Wi-Fi', category: 'Terminal', tier: 'Medium',
    symptoms: [{ layman: "The register just sits there — it won't let me ring anything up, like it's not talking to the system at all.", technical: 'Terminal.wifiNetwork ≠ Network.ssid — joined the wrong SSID.' }],
    faultModule: 'Terminal', faults: { wifiNetwork: 'SunriseDiner-Guest' },
    clues: [
      { actionId: 'check_terminal_network', text: `Terminal ▸ Network shows Wi-Fi "SunriseDiner-Guest" — Network Settings ▸ Connection Details shows the store's actual Wi-Fi is "${STORE_WIFI_SSID}".`, redHerring: false },
    ],
    resolution: { rootCause: [{ module: 'Terminal', field: 'wifiNetwork', op: 'Equals', value: STORE_WIFI_SSID }], requiresTestPass: false },
  },
  P7: {
    id: 'P7', title: "POS has a stale IP registered for the terminal", category: 'POS', tier: 'Medium',
    symptoms: [{ layman: "The register won't connect — it worked fine yesterday, nothing's changed on our end.", technical: "POSSoftware.registeredTerminalIp stale vs the terminal's actual DHCP-leased IP." }],
    // The terminal itself is fine (right Wi-Fi, correct DHCP-assigned IP) — the fault is on POS's SIDE:
    // its registered-terminal roster still has an old IP (e.g. from before a router reboot re-leased one).
    faultModule: 'POSSoftware', faults: { registeredTerminalIp: '192.168.1.77' },
    clues: [
      { actionId: 'check_terminal_network', text: `Terminal ▸ Network shows the terminal's actual IP is ${TERMINAL_IP_CORRECT} — POS Manager ▸ Connections still has 192.168.1.77 registered (stale).`, redHerring: false },
    ],
    resolution: { rootCause: [{ module: 'POSSoftware', field: 'registeredTerminalIp', op: 'Equals', value: TERMINAL_IP_CORRECT }], requiresTestPass: false },
  },
};

function freshDesktop() {
  return {
    // ssid: the store's actual Wi-Fi network name — reference value shown in Network Settings.
    Network: { isOnline: true, ssid: STORE_WIFI_SSID },
    // staffRole/staffTerminal/terminalSynced: real config for "Terminal phụ thuộc POS cấp quyền +
    // phiên đăng nhập" (GDD Mục 4) — the exact fix chain from GDD Mục 15's worked example
    // (assign role → assign terminal → sync POS→terminal), editable in POS Manager ▸ Staff Management.
    // dbHost: real config for "POS ... kết nối database" (GDD Mục 4) — editable in POS Manager ▸ Connections.
    // registeredTerminalIp: what POS has on file for this register — editable in POS Manager ▸ Connections.
    POSSoftware: { receiptTemplate: 'OK', staffRole: 'Admin', staffTerminal: 'REG-1', terminalSynced: true, dbHost: POS_DB_HOST_CORRECT, registeredTerminalIp: TERMINAL_IP_CORRECT },
    // wifiNetwork: the ONLY thing the player picks in Terminal ▸ Network — IP/gateway are derived
    // from it (terminalNetInfo), mirroring real DHCP.
    Terminal: { wifiNetwork: STORE_WIFI_SSID },
    Printer: { paperLevel: 'OK', driverState: 'OK', connection: 'Connected', port: 'COM3' },
    CashDrawer: { port: 'COM4' },
  };
}

/* ===================== SIMULATION LAYER ===================== */

function effectiveStatus(desktop, module) {
  switch (module) {
    case 'Network':
      return desktop.Network.isOnline ? { status: 'OK' } : { status: 'Error', reason: 'Network offline' };
    case 'POSSoftware': {
      const net = effectiveStatus(desktop, 'Network');
      if (net.status !== 'OK') return { status: 'Blocked', reason: 'cannot operate — reason: Network offline' };
      if (desktop.POSSoftware.receiptTemplate === 'Broken') return { status: 'Error', reason: 'Receipt template config broken' };
      return { status: 'OK' };
    }
    case 'Terminal': {
      // Terminal HARDWARE/SOFTWARE connectivity to POS — a network/software link, same for every
      // staff member. This must NOT depend on any one staff's login (GDD Mục 15: "Terminal hoàn
      // toàn khỏe" even while a specific staff member can't log in — two different failure domains).
      const pos = effectiveStatus(desktop, 'POSSoftware');
      if (pos.status === 'Blocked') return { status: 'Blocked', reason: 'cannot operate — reason: POS not connected' };
      // Own-fault states (this module's own misconfiguration) are 'Error', NOT 'Blocked' — 'Blocked' is
      // reserved for "something upstream is broken" (see Printer/CashDrawer below). Getting this right
      // matters: autoRevealApp() only suppresses clue reveal on 'Blocked' (an unreached upstream cause),
      // and evaluateIssue() only reports 'Hidden' on 'Blocked' — a local Error must stay diagnosable.
      if (desktop.Terminal.wifiNetwork !== desktop.Network.ssid) {
        return { status: 'Error', reason: `connected to the wrong Wi-Fi ("${desktop.Terminal.wifiNetwork}" instead of "${desktop.Network.ssid}") — wrong network means a completely different IP range, see Terminal ▸ Network` };
      }
      // Right network, but POS's registered IP roster is stale (e.g. a router reboot re-leased a new
      // IP after POS last registered this terminal) — a DIFFERENT, POS-side fault from the wrong-Wi-Fi
      // case above, fixed in POS Manager ▸ Connections instead of on the terminal itself.
      const actualIp = terminalNetInfo(desktop).ip;
      if (actualIp !== desktop.POSSoftware.registeredTerminalIp) {
        return { status: 'Error', reason: `IP mismatch — terminal is actually at ${actualIp}, POS has ${desktop.POSSoftware.registeredTerminalIp} registered` };
      }
      return { status: 'OK' };
    }
    case 'Printer': {
      const pos = effectiveStatus(desktop, 'POSSoftware');
      if (pos.status === 'Blocked') return { status: 'Blocked', reason: 'cannot operate — reason: POS not connected' };
      if (desktop.Printer.connection === 'Removed') return { status: 'Error', reason: 'Device removed' };
      if (desktop.Printer.driverState === 'Corrupted') return { status: 'Error', reason: 'Driver error (Code 39)' };
      if (desktop.Printer.paperLevel === 'Empty') return { status: 'Error', reason: 'Out of paper' };
      return { status: 'OK' };
    }
    case 'CashDrawer': {
      const printer = effectiveStatus(desktop, 'Printer');
      if (printer.status === 'Blocked') return { status: 'Blocked', reason: 'cannot operate — reason: POS not connected' };
      if (desktop.CashDrawer.port === desktop.Printer.port) return { status: 'Error', reason: `Port conflict with printer (${desktop.CashDrawer.port})` };
      return { status: 'OK' };
    }
  }
}

// Per-staff LOGIN status (GDD Mục 15) — completely separate from Terminal's own hardware/software
// connectivity above. A staff member can be denied login (no role / not assigned / not synced) while
// the Terminal itself is perfectly healthy, and vice versa (Terminal down blocks login for everyone).
function staffLoginStatus(desktop) {
  const term = effectiveStatus(desktop, 'Terminal');
  if (term.status !== 'OK') return { ok: false, reason: 'Terminal unreachable — ' + term.reason };
  const st = desktop.POSSoftware;
  if (!st.staffRole || st.staffRole === 'None') return { ok: false, reason: 'Login failed: permission denied by POS — no role assigned' };
  if (!st.staffTerminal) return { ok: false, reason: 'Login failed: permission denied by POS — not assigned to this terminal' };
  if (!st.terminalSynced) return { ok: false, reason: 'Login failed: assignment changed but not synced yet' };
  return { ok: true };
}

// Real DB connectivity check — depends on both the upstream chain AND the actual dbHost config
// the player can edit in POS Manager ▸ Connections (not a derived boolean, an actual field to get right).
function dbConnected(desktop) {
  const pos = effectiveStatus(desktop, 'POSSoftware');
  if (pos.status === 'Blocked') return { ok: false, reason: pos.reason };
  const host = (desktop.POSSoftware.dbHost || '').trim().toLowerCase();
  if (host !== POS_DB_HOST_CORRECT.toLowerCase()) {
    return { ok: false, reason: `Cannot resolve host "${desktop.POSSoftware.dbHost}"` };
  }
  return { ok: true };
}

function checkState(desktop, check) {
  const actual = desktop[check.module][check.field];
  if (check.op === 'Equals') return actual === check.value;
  if (check.op === 'NotEquals') return actual !== check.value;
  return false;
}

function runTest(desktop, testType) {
  const printerEs = effectiveStatus(desktop, 'Printer');
  if (testType === 'TestPage') return printerEs.status === 'OK';
  if (testType === 'CustomerCopy') return printerEs.status === 'OK' && desktop.POSSoftware.receiptTemplate === 'OK';
  return true;
}

function evaluateIssue(issue, desktop) {
  const es = effectiveStatus(desktop, issue.faultModule);
  if (es.status === 'Blocked') return 'Hidden';
  const rootOk = issue.resolution.rootCause.every(c => checkState(desktop, c));
  const testOk = !issue.resolution.requiresTestPass || runTest(desktop, issue.resolution.testType);
  if (rootOk && testOk) return 'Resolved';
  if (issue.id === 'P2' && desktop.Printer.connection === 'Removed') return 'MadeWorse';
  return 'Unresolved';
}

function evaluateTicket(ticket, desktop) {
  // An unauthorized transaction (Refund/Void processed without confirming the caller's authorization —
  // GDD "Caller Authorization") is a business-logic harm, same severity class as a technical MadeWorse:
  // it caps the ticket at Degraded no matter how cleanly every technical issue got fixed.
  if (ticket.unauthorizedActionTaken) return 'Degraded';
  const statuses = ticket.issueIds.map(id => evaluateIssue(ISSUES[id], desktop));
  if (statuses.includes('Hidden')) return 'InProgress';
  if (statuses.every(s => s === 'Resolved')) return 'Resolved';
  if (statuses.includes('MadeWorse')) return 'Degraded';
  return 'InProgress';
}

/* ===================== CAMPAIGN / CONFIG STATE ===================== */

const SAVE_KEY = 'pos_tech_support_save_v1';

let CONFIG = {
  shiftDurationSec: 150,
  totalDays: 60,
  minTotalTickets: 150,
  strikesPerNightFail: 3,
  warningsToGameOver: 3,
  ringTimeoutSec: 12,
};

let campaign = null; // {day, ticketsResolved, warnings, currency}

function defaultCampaign() {
  return { day: 1, ticketsResolved: 0, warnings: 0, currency: 0 };
}

function loadSave() {
  try {
    const raw = localStorage.getItem(SAVE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    return parsed;
  } catch (e) { return null; }
}

function persistSave() {
  localStorage.setItem(SAVE_KEY, JSON.stringify({ campaign, config: CONFIG }));
}

function resetSave() {
  localStorage.removeItem(SAVE_KEY);
  campaign = defaultCampaign();
  CONFIG = { shiftDurationSec: 150, totalDays: 60, minTotalTickets: 150, strikesPerNightFail: 3, warningsToGameOver: 3, ringTimeoutSec: 12 };
}

/* ===================== TICKET GENERATION ===================== */

function poolForDay(day) {
  if (day <= 5) return [['P1']];
  if (day <= 15) return [['P1'], ['P2']];
  if (day <= 30) return [['P1'], ['P2'], ['P3'], ['P6'], ['P7']];
  if (day <= 45) return [['P1'], ['P2'], ['P3'], ['P6'], ['P7'], ['P4', 'P1'], ['P4', 'P2']];
  return [['P1'], ['P2'], ['P3'], ['P5'], ['P6'], ['P7'], ['P4', 'P3'], ['P4', 'P5'], ['P4', 'P6']];
}

function pickIssueCombo(day) {
  const pool = poolForDay(day);
  return pool[Math.floor(Math.random() * pool.length)];
}

function ticketCountForDay(day) {
  const n = Math.round(2 + day * 0.05);
  return Math.max(1, Math.min(6, n));
}

let ticketSeq = 1;

function randomPick(correctVal, wrongVal, accuracy) {
  return Math.random() < accuracy ? correctVal : wrongVal;
}

function genPasscode() {
  const chars = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
  let s = '';
  for (let i = 0; i < 5; i++) s += chars[Math.floor(Math.random() * chars.length)];
  return s;
}

function makeTicket(day, forcedIssueIds) {
  const issueIds = forcedIssueIds || pickIssueCombo(day);
  const desktop = freshDesktop();
  issueIds.forEach(id => {
    const issue = ISSUES[id];
    Object.assign(desktop[issue.faultModule], issue.faults);
  });
  // Only tickets that are actually a refund/void scenario carry real authorization risk — everything
  // else defaults to fully authorized, so asking "Ask if owner authorized this" (or the Refund/Void
  // gate) never randomly ends a ticket that was never about a sensitive transaction to begin with.
  const isRefundVoidCase = Math.random() < 0.4;
  // callerRole is the ONE source of truth for who's on the phone — Owner speaks with the CRM's own name,
  // anything else just isn't the owner. No downstream code should re-derive this by comparing names.
  const callerRole = isRefundVoidCase ? 'Staff' : 'Owner';
  const callerName = callerRole === 'Owner'
    ? STORE.ownerName
    : STAFF_CALLER_NAMES[Math.floor(Math.random() * STAFF_CALLER_NAMES.length)];
  return {
    ticketId: 'TCK-' + String(ticketSeq++).padStart(4, '0'),
    day,
    issueIds,
    desktop,
    status: 'Queued', // Queued / Ringing / Active / Closed-Resolved / Closed-Degraded / Missed / Abandoned
    chat: [],
    sessionLog: [],
    callerRole,
    callerName,
    statedStoreName: randomPick(STORE.storeName, 'Sunset Diner', PERSONA.memoryAccuracy),
    statedOwnerName: callerRole === 'Owner' ? randomPick(STORE.ownerName, 'Maria Alvarado', PERSONA.memoryAccuracy) : callerName,
    statedMachineId: randomPick(STORE.machineId, 'REG-2', PERSONA.memoryAccuracy),
    crmQuery: '',
    crmResults: [],
    crmSelectedIndex: null,
    comparePending: null,
    compareResult: null,
    // Caller Authorization (GDD Mục 4): the caller isn't necessarily the owner — could be staff calling
    // on their behalf. callerAuthorized is the ground truth of whether the owner actually granted this —
    // only rolled for real on refund/void tickets (see isRefundVoidCase above), always true otherwise.
    // authorizationConfirmed only becomes true once the PLAYER establishes it (owner-name MATCH via
    // compare, or an explicit "yes" from asking) — never auto-set from callerAuthorized directly.
    isRefundVoidCase,
    callerAuthorized: isRefundVoidCase ? Math.random() < 0.5 : true,
    authorizationConfirmed: false,
    authorizationAsked: false,
    customerHungUp: false,
    unauthorizedActionTaken: false,
    remotePasscode: genPasscode(), // fresh one-time session code, like a real remote-support tool
    remoteConnectQuery: { id: '', pass: '' },
    remoteConnected: false,
    remoteConnectFailed: false,
    openAppKey: null,
    revealedActions: new Set(),
    appTabs: { possoftware: 'receipt', printer: 'queue', devicemanager: 'printer', cashdrawer: 'port', network: 'adapter', terminal: 'status' },
    batchId: 114,
    // Today's live batch — what Terminal shows. Cleared into dbArchive when the batch closes.
    transactions: [
      { type: 'Sale', amount: 12.50, status: 'Settled' },
      { type: 'Refund', amount: 4.00, status: 'Settled' },
      { type: 'Sale', amount: 8.25, status: 'Open' },
    ],
    // Persistent record (POS Manager ▸ Database) — survives batch close, keeps previous days too.
    dbArchive: [
      { day: 'Yesterday', type: 'Sale', amount: 22.00, status: 'Settled' },
      { day: 'Yesterday', type: 'Refund', amount: 5.00, status: 'Settled' },
      { day: '2 nights ago', type: 'Sale', amount: 9.75, status: 'Settled' },
    ],
    dbSelectedDay: 'Today',
    printJobs: [],
  };
}

/* ===================== NIGHT RUNTIME STATE ===================== */

// night = { day, ticketsTarget, spawnTimes, spawnedCount, elapsed, timer, ended,
//           queue: [] (spawned, waiting for the line to free up),
//           ringing: ticket|null (popup shown, waiting for Answer/Decline),
//           active: ticket|null (answered, ticket window open),
//           history: [] (finished calls this night, for the call log),
//           strikes, harmEvents }
let night = null;

/* ===================== DOM HELPERS ===================== */

const $ = sel => document.querySelector(sel);
const screens = {
  hub: $('#screen-hub'), night: $('#screen-night'), endofnight: $('#screen-endofnight'),
  gameover: $('#screen-gameover'), win: $('#screen-win'),
};

function showScreen(name) {
  Object.values(screens).forEach(s => s.classList.add('hidden'));
  screens[name].classList.remove('hidden');
}

/* ===================== HUB RENDER ===================== */

function renderHub() {
  $('#hub-day').textContent = campaign.day;
  $('#hub-totaldays').textContent = CONFIG.totalDays;
  $('#hub-tickets').textContent = campaign.ticketsResolved;
  $('#hub-mintickets').textContent = CONFIG.minTotalTickets;
  $('#hub-warnings').textContent = campaign.warnings;
  $('#hub-maxwarnings').textContent = CONFIG.warningsToGameOver;
  $('#hub-currency').textContent = campaign.currency;

  $('#cfg-shift-sec').value = CONFIG.shiftDurationSec;
  $('#cfg-total-days').value = CONFIG.totalDays;
  $('#cfg-min-tickets').value = CONFIG.minTotalTickets;
  $('#cfg-strikes').value = CONFIG.strikesPerNightFail;
  $('#cfg-warnings').value = CONFIG.warningsToGameOver;
  $('#cfg-ring-timeout').value = CONFIG.ringTimeoutSec;

  showScreen('hub');
}

$('#btn-apply-cfg').addEventListener('click', () => {
  CONFIG.shiftDurationSec = Number($('#cfg-shift-sec').value) || 150;
  CONFIG.totalDays = Number($('#cfg-total-days').value) || 60;
  CONFIG.minTotalTickets = Number($('#cfg-min-tickets').value) || 150;
  CONFIG.strikesPerNightFail = Number($('#cfg-strikes').value) || 3;
  CONFIG.warningsToGameOver = Number($('#cfg-warnings').value) || 3;
  CONFIG.ringTimeoutSec = Number($('#cfg-ring-timeout').value) || 12;
  persistSave();
  renderHub();
});

$('#btn-reset-save').addEventListener('click', () => {
  if (!confirm('Reset the whole campaign (day, tickets, warnings, money)?')) return;
  resetSave();
  renderHub();
});

$('#btn-restart').addEventListener('click', () => { resetSave(); renderHub(); });
$('#btn-restart-win').addEventListener('click', () => { resetSave(); renderHub(); });

/* ===================== NIGHT FLOW ===================== */

$('#btn-start-night').addEventListener('click', startNight);

function startNight() {
  const day = campaign.day;
  const ticketsTarget = ticketCountForDay(day);
  const spawnFractions = Array.from({ length: ticketsTarget }, () => Math.pow(Math.random(), 1.4)).sort((a, b) => a - b);
  night = {
    day,
    ticketsTarget,
    spawnTimes: spawnFractions.map(f => f * CONFIG.shiftDurationSec),
    spawnedCount: 0,
    elapsed: 0,
    queue: [],
    ringing: null,
    active: null,
    history: [],
    strikes: 0,
    harmEvents: [],
    ended: false,
  };
  $('#night-day').textContent = day;
  $('#night-strikes-max').textContent = CONFIG.strikesPerNightFail;
  renderCallLog();
  showScreen('night');
  night.timer = setInterval(tickNight, 250);
}

$('#btn-dev-force-call').addEventListener('click', () => {
  if (!night || night.ended) return;
  if (night.active) { alert('End the current call first.'); return; }
  if (night.ringing) { alert('Answer or decline the ringing call first.'); return; }
  const val = $('#dev-issue-picker').value;
  const issueIds = val === 'random' ? null : val.split(',');
  const t = makeTicket(night.day, issueIds);
  answerCall(t);
});

function formatClock(elapsed) {
  const hour24 = 20 + (elapsed / CONFIG.shiftDurationSec) * 8;
  const wrapped = hour24 % 24;
  let h = Math.floor(wrapped);
  const m = Math.floor((wrapped - h) * 60);
  const ampm = h >= 12 ? 'PM' : 'AM';
  let h12 = h % 12; if (h12 === 0) h12 = 12;
  return `${h12}:${String(m).padStart(2, '0')} ${ampm}`;
}

function formatDuration(sec) {
  const s = Math.max(0, Math.floor(sec));
  const m = Math.floor(s / 60);
  const r = s % 60;
  return `${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}`;
}

function tickNight() {
  night.elapsed += 0.25;
  $('#night-clock').textContent = formatClock(night.elapsed);
  $('#night-count').textContent = night.spawnedCount;
  $('#night-strikes').textContent = night.harmEvents.length;
  $('#night-waiting').textContent = night.queue.length + (night.ringing ? 1 : 0);
  const pct = Math.min(100, (night.elapsed / CONFIG.shiftDurationSec) * 100);
  $('#night-progress').style.width = pct + '%';

  while (night.spawnedCount < night.spawnTimes.length && night.elapsed >= night.spawnTimes[night.spawnedCount]) {
    const t = makeTicket(night.day);
    night.queue.push(t);
    night.spawnedCount++;
  }

  maybeStartNextCall();

  if (night.ringing) {
    const remaining = night.ringing.ringDeadline - night.elapsed;
    const pctLeft = Math.max(0, Math.min(100, (remaining / CONFIG.ringTimeoutSec) * 100));
    $('#ring-timeout-bar').style.width = pctLeft + '%';
    if (remaining <= 0) missCall(night.ringing, 'no answer');
  }

  if (night.active) {
    $('#tk-call-timer').textContent = formatDuration(night.elapsed - night.active.answeredAtElapsed);
  }

  renderCallLog();

  if (night.elapsed >= CONFIG.shiftDurationSec && !night.ended) {
    endNight();
  }
}

function maybeStartNextCall() {
  if (night.ended || night.ringing || night.active || !night.queue.length) return;
  const t = night.queue.shift();
  t.status = 'Ringing';
  t.ringDeadline = night.elapsed + CONFIG.ringTimeoutSec;
  night.ringing = t;
  showIncomingCall(t);
}

function showIncomingCall(t) {
  $('#incoming-call-overlay').classList.remove('hidden');
  $('#ring-timeout-bar').style.width = '100%';
}

function hideIncomingCall() {
  $('#incoming-call-overlay').classList.add('hidden');
}

function missCall(ticket, reason) {
  ticket.status = 'Missed';
  night.harmEvents.push(`Missed call from ${STORE.storeName} (${reason}) — customer complaint filed.`);
  night.history.push(ticket);
  night.ringing = null;
  hideIncomingCall();
  renderCallLog();
  maybeStartNextCall();
}

$('#btn-answer-call').addEventListener('click', () => {
  if (!night || !night.ringing) return;
  const t = night.ringing;
  night.ringing = null;
  hideIncomingCall();
  answerCall(t);
});

$('#btn-decline-call').addEventListener('click', () => {
  if (!night || !night.ringing) return;
  missCall(night.ringing, 'declined by agent');
});

function renderCallLog() {
  const el = $('#call-log');
  el.innerHTML = night.history.map(t => {
    const statusClass = t.status.startsWith('Closed') ? t.status : t.status;
    return `<div class="call-log-entry">
      <span class="tc-id">${t.ticketId}</span>
      <span>${STORE.storeName}</span>
      <span class="tc-status status-${statusClass}">${t.status}</span>
    </div>`;
  }).join('');
  const idle = !night.history.length && !night.ringing && !night.active;
  $('#night-hint').style.display = idle ? 'block' : 'none';
}

function endNight() {
  night.ended = true;
  clearInterval(night.timer);
  hideIncomingCall();
  $('#ticket-modal').classList.add('hidden');
  $('#app-window-overlay').classList.add('hidden');
  $('#remote-screen-overlay').classList.add('hidden');

  if (night.ringing) {
    night.ringing.status = 'Missed';
    night.harmEvents.push(`Missed call from ${STORE.storeName} (shift ended before it was answered).`);
    night.history.push(night.ringing);
    night.ringing = null;
  }
  if (night.active) {
    const t = night.active;
    const finalStatus = evaluateTicket(t, t.desktop);
    if (finalStatus === 'Resolved') {
      t.status = 'Closed-Resolved';
    } else if (finalStatus === 'Degraded') {
      t.status = 'Closed-Degraded';
      night.harmEvents.push(`Ticket ${t.ticketId} closed degraded — customer complaint filed.`);
    } else {
      t.status = 'Abandoned';
      night.harmEvents.push(`Call with ${STORE.storeName} was cut off by end of shift — customer complaint filed.`);
    }
    night.history.push(t);
    night.active = null;
  }
  night.queue.forEach(t => {
    t.status = 'Missed';
    night.harmEvents.push(`Missed call from ${STORE.storeName} (never reached before shift ended).`);
    night.history.push(t);
  });
  night.queue = [];

  night.strikes = night.harmEvents.length;

  const resolvedCount = night.history.filter(t => t.status === 'Closed-Resolved').length;
  const degradedCount = night.history.filter(t => t.status === 'Closed-Degraded').length;
  const earned = resolvedCount * 10 - degradedCount * 15;

  campaign.ticketsResolved += resolvedCount;
  campaign.currency += Math.max(0, earned);

  const nightFailed = night.strikes >= CONFIG.strikesPerNightFail;
  if (nightFailed) campaign.warnings += 1;

  renderCallLog();
  renderEndOfNight(resolvedCount, degradedCount, nightFailed);
  persistSave();
}

function renderEndOfNight(resolvedCount, degradedCount, nightFailed) {
  $('#eon-day').textContent = campaign.day;
  const list = $('#eon-summary');
  list.innerHTML = `
    <li>Calls tonight: ${night.history.length}</li>
    <li>Resolved cleanly: ${resolvedCount}</li>
    <li>Degraded / made worse: ${degradedCount}</li>
    <li>Complaint mails this night: ${night.harmEvents.length}</li>
    <li>Paycheck earned: $${campaign.currency}</li>
  `;
  if (night.harmEvents.length) {
    const sub = document.createElement('li');
    sub.innerHTML = '<i>' + night.harmEvents.join('<br>') + '</i>';
    list.appendChild(sub);
  }
  const verdict = $('#eon-verdict');
  if (nightFailed) {
    verdict.className = 'eon-verdict fail';
    verdict.textContent = `Night FAILED (${night.strikes} strikes) — +1 warning. Total warnings: ${campaign.warnings}/${CONFIG.warningsToGameOver}`;
  } else {
    verdict.className = 'eon-verdict ok';
    verdict.textContent = 'Night passed.';
  }
  showScreen('endofnight');
}

$('#btn-continue').addEventListener('click', () => {
  if (campaign.warnings > CONFIG.warningsToGameOver) {
    $('#gameover-text').textContent = `You've accumulated ${campaign.warnings} warnings and been let go.`;
    showScreen('gameover');
    return;
  }
  campaign.day += 1;
  if (campaign.day > CONFIG.totalDays) {
    if (campaign.ticketsResolved >= CONFIG.minTotalTickets) {
      $('#win-text').textContent = `You completed ${CONFIG.totalDays} days and resolved ${campaign.ticketsResolved} tickets. Welcome aboard, full-time.`;
      showScreen('win');
    } else {
      $('#gameover-text').textContent = `Probation period ended without hitting quota (${campaign.ticketsResolved}/${CONFIG.minTotalTickets} tickets). You've been let go.`;
      showScreen('gameover');
    }
    persistSave();
    return;
  }
  persistSave();
  renderHub();
});

/* ===================== TICKET WINDOW (on-call) ===================== */

function answerCall(ticket) {
  ticket.status = 'Active';
  ticket.answeredAtElapsed = night.elapsed;
  night.active = ticket;

  $('#tk-id').textContent = ticket.ticketId;
  $('#tk-store').textContent = STORE.storeName;
  $('#tk-call-timer').textContent = '00:00';

  if (!ticket.chat.length) {
    const firstIssue = ISSUES[ticket.issueIds[0]];
    pushChat(ticket, 'customer', `Hi, this is ${ticket.callerName} from ${STORE.storeName}. ${firstIssue.symptoms[0].layman}`);
  }

  renderTicket();
  $('#ticket-modal').classList.remove('hidden');
}

function finishActiveCall(ticket, status) {
  ticket.status = status;
  ticket.openAppKey = null;
  night.history.push(ticket);
  night.active = null;
  $('#app-window-overlay').classList.add('hidden');
  $('#remote-screen-overlay').classList.add('hidden');
  $('#ticket-modal').classList.add('hidden');
  renderCallLog();
  maybeStartNextCall();
}

// fact: optional { type: 'storeName'|'ownerName'|'machineId', value } — lets this chat line be used as
// one side of the click-to-compare mechanic against the matching CRM field.
function pushChat(ticket, kind, text, fact) {
  ticket.chat.push({ kind, text, fact });
}

function pushSessionLog(ticket, kind, text) {
  ticket.sessionLog.push({ kind, text });
}

function renderChat(ticket) {
  const el = $('#chat-log');
  el.innerHTML = ticket.chat.map(c => {
    if (c.kind === 'customer') {
      const line = `<span class="who">${ticket.callerName}:</span> ${c.text}`;
      if (c.fact) return compareFieldHtml(ticket, 'chat', c.fact.type, c.fact.value, line);
      return `<div>${line}</div>`;
    }
    if (c.kind === 'sms') return `<div class="sms">[SMS] ${c.text}</div>`;
    return `<div>${c.text}</div>`;
  }).join('');
  el.scrollTop = el.scrollHeight;
  wireCompareButtons(ticket);
}

function openRemoteScreen(ticket) {
  if (!ticket.remoteConnected) return;
  $('#remote-screen-overlay').classList.remove('hidden');
  renderRemoteScreenWindow(ticket);
}

function closeRemoteScreen() {
  $('#remote-screen-overlay').classList.add('hidden');
  closeApp();
}
$('#remote-screen-close').addEventListener('click', closeRemoteScreen);

function renderRemoteScreenWindow(ticket) {
  const el = $('#remote-screen-body');
  const icons = Object.entries(APP_DEFS).map(([key, def]) =>
    `<button class="rd-icon" id="icon-${key}"><span class="rd-icon-glyph">${def.icon}</span>${def.title}</button>`
  ).join('');
  el.innerHTML = `<div class="rd-desktop">
    <div class="rd-icons">${icons}</div>
    <div class="rd-taskbar"><span class="rd-start-btn">⊞ Start</span><span>${STORE.machineId} — Connected</span></div>
  </div>`;
  Object.keys(APP_DEFS).forEach(key => {
    document.getElementById('icon-' + key).addEventListener('click', () => openApp(ticket, key));
  });
}

function latestTransactionFor(ticket) {
  if (!ticket.transactions.length) return null;
  const open = [...ticket.transactions].reverse().find(t => t.status === 'Open');
  return open || ticket.transactions[ticket.transactions.length - 1];
}

// Reveals a Diagnostic action's clue the first time it fires this ticket, from ANY app that triggers it.
function revealClueOnce(ticket, actionId) {
  if (ticket.revealedActions.has(actionId)) return;
  ticket.revealedActions.add(actionId);
  ticket.issueIds.forEach(id => {
    ISSUES[id].clues.filter(c => c.actionId === actionId).forEach(c => {
      pushSessionLog(ticket, c.redHerring ? 'herring' : 'clue', c.text);
    });
  });
}

// Records live in 2 places at once (GDD "Transaction data model"): today's live batch (ticket.transactions,
// shown on Terminal) and the persistent archive (ticket.dbArchive, shown in POS Manager ▸ Database).
function recordsForDay(ticket, day) {
  if (day === 'Today') return [...ticket.transactions, ...ticket.dbArchive.filter(r => r.day === 'Today')];
  return ticket.dbArchive.filter(r => r.day === day);
}

function availableDbDays(ticket) {
  const days = ['Today'];
  ticket.dbArchive.forEach(r => { if (!days.includes(r.day)) days.push(r.day); });
  return days;
}

// Printing Merchant/Customer/Store receipts needs real transaction data (per GDD, unlike Test Page) —
// triggered from POS Manager ▸ Database, since POS Software owns the receipt template + DB connection.
function printReceiptFor(ticket, record, docType) {
  if (docType === 'Customer Copy') revealClueOnce(ticket, 'print_customer_copy');
  const pass = runTest(ticket.desktop, 'CustomerCopy');
  let reason = '';
  if (!pass) {
    const printerEs = effectiveStatus(ticket.desktop, 'Printer');
    reason = printerEs.status !== 'OK' ? printerEs.reason : 'receipt template misconfigured — see Receipt Template tab';
  }
  record.lastPrintResult = pass ? `PASS (${docType})` : `FAIL (${docType}) — ${reason}`;
  ticket.printJobs.push({ doc: `${docType} — ${record.type} $${record.amount.toFixed(2)}`, status: pass ? 'Printed' : 'Error' });
  pushSessionLog(ticket, 'result', `Print ${docType} for ${record.type} $${record.amount.toFixed(2)} (${record.day}): ${pass ? 'PASS' : 'FAIL — ' + reason}`);
}

function runAction(ticket, action) {
  if (action.risky) {
    if (!confirm(action.riskyWarning || 'This action is risky. Continue?')) return;
  }

  if (action.kind === 'Diagnostic') {
    let any = false;
    ticket.issueIds.forEach(id => {
      ISSUES[id].clues.filter(c => c.actionId === action.id).forEach(c => {
        any = true;
        pushSessionLog(ticket, c.redHerring ? 'herring' : 'clue', c.text);
      });
    });
    if (action.isTest) {
      const pass = runTest(ticket.desktop, action.testType);
      let reason = '';
      if (!pass) {
        const printerEs = effectiveStatus(ticket.desktop, 'Printer');
        if (printerEs.status !== 'OK') reason = printerEs.reason;
      }
      pushSessionLog(ticket, 'result', `${action.label}: ${pass ? 'PASS' : 'FAIL'}${pass ? '' : ' — ' + reason}`);
      any = true;
    }
    if (!any) pushSessionLog(ticket, 'result', `${action.label}: nothing unusual found.`);
  } else {
    Object.entries(action.changes).forEach(([field, value]) => {
      ticket.desktop[action.target][field] = value;
    });
    pushSessionLog(ticket, 'result', `${action.label}: applied.`);
  }
}

/* ---- generic app window (desktop apps only — CRM & Remote Software are now inline in the ticket window) ---- */

function openApp(ticket, key) {
  ticket.openAppKey = key;
  $('#app-window-overlay').classList.remove('hidden');
  renderOpenApp();
}

function closeApp() {
  if (night.active) night.active.openAppKey = null;
  $('#app-window-overlay').classList.add('hidden');
}
$('#app-window-close').addEventListener('click', closeApp);

function renderOpenApp() {
  const ticket = night.active;
  if (!ticket || !ticket.openAppKey) return;
  const key = ticket.openAppKey;
  const def = APP_DEFS[key];
  $('#app-window-title').textContent = def.title;
  $('#app-window-body').innerHTML = moduleAppHtml(ticket, key);
  wireModuleApp(ticket, key);
}

/* ---- CRM search (inline, tk-mid) ----
   Search by store ID or store name, fuzzy — can return MULTIPLE hits (real accounts have similar
   names). Clicking a result shows THAT record's full info, including its own remote credentials.
   Picking the wrong one just fails to connect later — no hard-blocking exact-match softlock. */

const COMPARE_FIELD_LABEL = { storeName: 'Store Name', ownerName: 'Owner Name', machineId: 'Machine ID' };

// Click-to-compare status banner — shown once at the top of the CRM panel. Nothing is auto-flagged;
// the player has to actively pick one fact from the CRM record and its counterpart from the chat log
// (in either order) before any match/mismatch verdict appears.
function compareStatusHtml(ticket) {
  if (ticket.compareResult) {
    const r = ticket.compareResult;
    return `<div class="compare-status ${r.match ? 'match' : 'mismatch'}">
      ${r.match ? '✓ MATCH' : '✗ MISMATCH'} — ${COMPARE_FIELD_LABEL[r.type]}: CRM says "${r.crmValue}", customer said "${r.chatValue}"
    </div>`;
  }
  if (ticket.comparePending) {
    const p = ticket.comparePending;
    const otherSide = p.source === 'crm' ? 'the matching line in Customer Chat' : 'the matching field in the CRM record';
    return `<div class="compare-status pending">🔍 Selected ${COMPARE_FIELD_LABEL[p.type]}: "${p.value}" — click ${otherSide} to compare.</div>`;
  }
  return '';
}

// The info row/chat line itself IS the button — no separate "Compare" pill to hunt for and click.
function compareFieldHtml(ticket, source, type, value, innerHtml) {
  const p = ticket.comparePending;
  const isPending = p && p.source === source && p.type === type && p.value === value;
  return `<button class="compare-field ${isPending ? 'pending' : ''}" data-compare-source="${source}" data-compare-type="${type}" data-compare-value="${value}">${innerHtml}</button>`;
}

// Two clicks (CRM field + matching chat statement, either order) → one verdict. Clicking two items from
// the SAME source, or of a DIFFERENT type, just replaces the pending selection instead of comparing —
// nothing is ever auto-compared or auto-flagged for the player.
function handleCompareClick(ticket, clicked) {
  const pending = ticket.comparePending;
  if (pending && pending.source !== clicked.source && pending.type === clicked.type) {
    const crmItem = pending.source === 'crm' ? pending : clicked;
    const chatItem = pending.source === 'chat' ? pending : clicked;
    const match = crmItem.value.trim().toLowerCase() === chatItem.value.trim().toLowerCase();
    ticket.compareResult = { type: clicked.type, crmValue: crmItem.value, chatValue: chatItem.value, match };
    ticket.comparePending = null;
    // A confirmed MATCH on the owner's own name is exactly what real caller-ID + CRM lookup proves in
    // real life: this person IS who they say they are. That alone establishes authorization — no need
    // to separately ask, mirroring how "Ask if owner authorized this" grants it when they say yes.
    if (clicked.type === 'ownerName' && match) ticket.authorizationConfirmed = true;
  } else {
    ticket.comparePending = clicked;
    ticket.compareResult = null;
  }
  renderTicket();
}

function wireCompareButtons(ticket) {
  document.querySelectorAll('.compare-field').forEach(btn => {
    btn.addEventListener('click', () => handleCompareClick(ticket, {
      source: btn.dataset.compareSource,
      type: btn.dataset.compareType,
      value: btn.dataset.compareValue,
    }));
  });
}

function crmSearchHtml(ticket) {
  let html = compareStatusHtml(ticket);
  html += `
    <div class="field-row">
      <input type="text" id="crm-search-input" placeholder="Store ID or store name..." value="${ticket.crmQuery}">
      <button id="btn-crm-search" class="btn">Search</button>
    </div>`;
  if (ticket.crmQuery.trim()) {
    if (!ticket.crmResults.length) {
      html += '<p class="hint">No matches.</p>';
    } else {
      html += '<div class="crm-result-list">' + ticket.crmResults.map((r, i) => `
        <button class="crm-result-row ${ticket.crmSelectedIndex === i ? 'selected' : ''}" data-crm-index="${i}">
          <b>${r.storeName}</b><br><span class="hint">${r.storeId} — ${r.address}</span>
        </button>`).join('') + '</div>';
    }
  }
  const record = ticket.crmSelectedIndex != null ? ticket.crmResults[ticket.crmSelectedIndex] : null;
  if (record) {
    const passcode = record.isReal ? ticket.remotePasscode : record.fixedPasscode;
    html += `
      <div class="crm-record" style="margin-top:10px;">
        <hr>
        <div>Store ID: <b>${record.storeId}</b></div>
        ${compareFieldHtml(ticket, 'crm', 'storeName', record.storeName, `Store Name: <b>${record.storeName}</b>`)}
        <div>Address: <b>${record.address}</b></div>
        ${compareFieldHtml(ticket, 'crm', 'ownerName', record.ownerName, `Owner: <b>${record.ownerName}</b>`)}
        ${compareFieldHtml(ticket, 'crm', 'machineId', record.machineId, `Machine on file: <b>${record.machineId}</b>`)}
        <div class="crm-remote-box">
          <div><b>Remote Access Credentials</b></div>
          <div>Remote ID: <b>${record.remoteId}</b></div>
          <div>Passcode: <b>${passcode}</b></div>
        </div>
      </div>`;
  }
  return html;
}

function wireCrmSearch(ticket) {
  const input = document.getElementById('crm-search-input');
  const btn = document.getElementById('btn-crm-search');
  const runSearch = () => {
    ticket.crmQuery = input.value;
    ticket.crmResults = searchCrmDirectory(ticket.crmQuery);
    ticket.crmSelectedIndex = null;
    renderCrmSearchPanel(ticket);
  };
  if (btn) btn.addEventListener('click', runSearch);
  if (input) input.addEventListener('keydown', e => { if (e.key === 'Enter') runSearch(); });
  document.querySelectorAll('#crm-panel-body [data-crm-index]').forEach(rowBtn => {
    rowBtn.addEventListener('click', () => {
      ticket.crmSelectedIndex = Number(rowBtn.dataset.crmIndex);
      renderCrmSearchPanel(ticket);
    });
  });
}

function renderCrmSearchPanel(ticket) {
  $('#crm-panel-body').innerHTML = crmSearchHtml(ticket);
  wireCrmSearch(ticket);
  wireCompareButtons(ticket);
}

/* ---- Remote Desktop Software connect form (inline, tk-right) ---- */

function remoteConnectFormHtml(ticket) {
  // Always the same ID/passcode form, even once connected — closing the viewer window doesn't clear
  // remoteConnectQuery, so the fields stay filled in and hitting Connect again just reopens the screen.
  const q = ticket.remoteConnectQuery;
  let html = ticket.remoteConnected ? '<div class="connect-ok">✓ Connected to ' + STORE.machineId + '.</div>' : '';
  html += `
    <div class="connect-form">
      <label class="field">Remote ID
        <input type="text" id="rs-in-id" value="${q.id}">
      </label>
      <label class="field">Passcode
        <input type="text" id="rs-in-pass" value="${q.pass}">
      </label>
      <button id="btn-rs-connect" class="btn btn-primary">Connect</button>
    </div>`;
  if (ticket.remoteConnectFailed) {
    html += '<div class="connect-error">Connection failed — wrong Remote ID/passcode. Double-check you picked the right CRM record — use 🔍 Compare against what the customer said.</div>';
  }
  return html;
}

function wireRemoteConnectForm(ticket) {
  const btn = document.getElementById('btn-rs-connect');
  if (!btn) return;
  btn.addEventListener('click', () => {
    const id = document.getElementById('rs-in-id').value.trim();
    const pass = document.getElementById('rs-in-pass').value.trim();
    ticket.remoteConnectQuery = { id, pass };
    const ok = id === STORE.remoteId && pass.toLowerCase() === ticket.remotePasscode.toLowerCase();
    if (ok) {
      ticket.remoteConnected = true;
      ticket.remoteConnectFailed = false;
      renderRemoteConnectPanel(ticket);
      renderTicket();
      openRemoteScreen(ticket);
    } else {
      ticket.remoteConnectFailed = true;
      renderRemoteConnectPanel(ticket);
    }
  });
}

function renderRemoteConnectPanel(ticket) {
  $('#remote-connect-form').innerHTML = remoteConnectFormHtml(ticket);
  wireRemoteConnectForm(ticket);
}

// Diagnostic (non-test) actions reveal their clue the moment the app is opened —
// a real Device Manager/print queue/etc. just shows you its state, no extra click needed.
function autoRevealApp(ticket, appKey) {
  const def = APP_DEFS[appKey];
  const es = effectiveStatus(ticket.desktop, def.targetModule);
  if (es.status === 'Blocked') return;
  ACTIONS.filter(a => a.app === appKey && a.kind === 'Diagnostic' && !a.isTest).forEach(a => {
    if (ticket.revealedActions.has(a.id)) return;
    ticket.revealedActions.add(a.id);
    ticket.issueIds.forEach(id => {
      ISSUES[id].clues.filter(c => c.actionId === a.id).forEach(c => {
        pushSessionLog(ticket, c.redHerring ? 'herring' : 'clue', c.text);
      });
    });
  });
}

function fixButtonHtml(ticket, actionId, label) {
  const a = ACTIONS.find(x => x.id === actionId);
  const es = effectiveStatus(ticket.desktop, a.target);
  let disabled = es.status === 'Blocked';
  let reason = disabled ? es.reason : '';
  if (!disabled && a.pre && !a.pre.every(c => checkState(ticket.desktop, { module: a.target, ...c }))) {
    disabled = true; reason = 'precondition not met';
  }
  return `<button id="act-${a.id}" class="action-btn ${a.risky ? 'risky' : ''}" ${disabled ? 'disabled' : ''} title="${reason}">${label || a.label}</button>`;
}

// Real per-app "connection to its upstream" indicator + Retry button (per GDD Mục 4 dependency graph:
// Network → POS Software (HUB) → Terminal/Database/Printer → Cash Drawer). Retry doesn't fake-fix anything —
// it just re-tests the live link, exactly like a real client reconnect button. It only succeeds once the
// actual upstream root cause has been fixed in ITS OWN app.
function connectionPanelHtml(appLabel, module, es) {
  return `<div class="conn-panel">
    <div class="conn-row">🔌 ${appLabel} link to POS Software (HUB): <b class="text-err">Disconnected</b></div>
    <div class="conn-reason">Reason: ${es.reason}</div>
    <button class="action-btn" data-reconnect-module="${module}" data-reconnect-label="${appLabel}">Retry Connection</button>
  </div>`;
}

function wireConnectionButtons(ticket) {
  document.querySelectorAll('#app-window-body [data-reconnect-module]').forEach(btn => {
    btn.addEventListener('click', () => {
      const mod = btn.dataset.reconnectModule;
      const label = btn.dataset.reconnectLabel;
      const es = effectiveStatus(ticket.desktop, mod);
      if (es.status !== 'Blocked') {
        pushSessionLog(ticket, 'result', `${label}: connection OK.`);
      } else {
        pushSessionLog(ticket, 'result', `${label}: reconnect attempt failed — ${es.reason}.`);
      }
      renderOpenApp();
      renderTicket();
    });
  });
}

function tabBarHtml(tabs, activeKey) {
  return `<div class="win-tabs">${tabs.map(t =>
    `<button class="win-tab ${activeKey === t.key ? 'active' : ''}" data-tab="${t.key}">${t.label}</button>`
  ).join('')}</div>`;
}

function moduleAppHtml(ticket, appKey) {
  autoRevealApp(ticket, appKey);
  switch (appKey) {
    case 'printer': return printerAppHtml(ticket);
    case 'devicemanager': return deviceManagerAppHtml(ticket);
    case 'cashdrawer': return cashDrawerAppHtml(ticket);
    case 'network': return networkAppHtml(ticket);
    case 'possoftware': return posManagerAppHtml(ticket);
    case 'terminal': return terminalAppHtml(ticket);
  }
}

/* ---- Printer & Print Queue ---- */
const PRINTER_TABS = [
  { key: 'queue', label: 'Print Queue' },
  { key: 'properties', label: 'Printer Properties' },
  { key: 'receipts', label: 'Receipt Types' },
];

function printerAppHtml(ticket) {
  const es = effectiveStatus(ticket.desktop, 'Printer');
  const tab = ticket.appTabs.printer;
  let body;
  if (tab === 'properties') body = printerPropertiesTab(ticket);
  else if (tab === 'receipts') body = printerReceiptTypesTab();
  else body = printerQueueTab(ticket);
  const conn = es.status === 'Blocked' ? connectionPanelHtml('Printer', 'Printer', es) : '';
  return tabBarHtml(PRINTER_TABS, tab) + conn + body;
}

function printerQueueTab(ticket) {
  const d = ticket.desktop.Printer;
  const cashDrawerOk = effectiveStatus(ticket.desktop, 'CashDrawer').status !== 'Error';
  const rows = [];
  if (d.connection === 'Removed' || d.driverState === 'Corrupted') {
    rows.push('<tr><td>(any job)</td><td class="text-err">Error — device not responding</td><td>SYSTEM</td></tr>');
  } else if (d.paperLevel === 'Empty') {
    rows.push('<tr><td>(any job)</td><td class="text-err">Error — out of paper</td><td>SYSTEM</td></tr>');
  }
  ticket.printJobs.slice().reverse().forEach(job => {
    rows.push(`<tr><td>${job.doc}</td><td class="${job.status === 'Printed' ? 'text-ok' : 'text-err'}">${job.status}</td><td>Terminal</td></tr>`);
  });
  if (!rows.length) rows.push('<tr><td colspan="3"><i>No documents in queue</i></td></tr>');
  return `
    <div class="win-toolbar">🖨 Epson TM-T88 Receipt Printer</div>
    <table class="win-table">
      <thead><tr><th>Document</th><th>Status</th><th>Owner</th></tr></thead>
      <tbody>${rows.join('')}</tbody>
    </table>
    <div class="printer-meta">Paper tray: <b class="${d.paperLevel === 'OK' ? 'text-ok' : 'text-err'}">${d.paperLevel}</b></div>
    <div class="printer-meta">Cash drawer will open on print: <b class="${cashDrawerOk ? 'text-ok' : 'text-err'}">${cashDrawerOk ? 'Yes' : 'No — port conflict'}</b></div>
    <p class="hint">Receipts are requested from POS Manager ▸ Database — this queue just shows what came through.</p>
    <div class="app-actions">
      ${fixButtonHtml(ticket, 'print_test_page', 'Print test page')}
      ${fixButtonHtml(ticket, 'refill_paper_tray', 'Refill paper tray')}
    </div>`;
}

function printerPropertiesTab(ticket) {
  const d = ticket.desktop.Printer;
  const spoolerRunning = d.driverState !== 'Corrupted' && d.connection !== 'Removed';
  return `
    <div class="config-row">Port: <b>${d.port}</b></div>
    <div class="config-row">Connection: <b class="${d.connection === 'Connected' ? 'text-ok' : 'text-err'}">${d.connection}</b></div>
    <div class="config-row">Driver: <b class="${d.driverState === 'OK' ? 'text-ok' : 'text-err'}">${d.driverState}</b></div>
    <div class="config-row">Spooler service: <b class="${spoolerRunning ? 'text-ok' : 'text-err'}">${spoolerRunning ? 'Running' : 'Stopped'}</b></div>`;
}

function printerReceiptTypesTab() {
  return `
    <table class="win-table">
      <thead><tr><th>Type</th><th>Needs transaction data?</th><th>Purpose</th></tr></thead>
      <tbody>
        <tr><td>Test Page</td><td>No</td><td>Checks hardware/driver only</td></tr>
        <tr><td>Merchant Receipt</td><td>Yes</td><td>Store's copy of the sale</td></tr>
        <tr><td>Customer Copy</td><td>Yes</td><td>Customer's copy of the sale</td></tr>
        <tr><td>Store Receipt</td><td>Yes</td><td>Internal record copy</td></tr>
      </tbody>
    </table>
    <p class="hint">Test Page is printed here (no transaction needed). Merchant/Customer/Store receipts are requested from POS Manager ▸ Database, since they need a real transaction. Test Page can PASS even when those are wrong — hardware ≠ software config.</p>`;
}

/* ---- Device Manager ---- */
const DEVICEMGR_TABS = [
  { key: 'printer', label: 'Printer Properties' },
  { key: 'devices', label: 'All Devices' },
];

function deviceManagerAppHtml(ticket) {
  const es = effectiveStatus(ticket.desktop, 'Printer');
  const tab = ticket.appTabs.devicemanager;
  const body = tab === 'devices' ? deviceManagerDevicesTab(ticket) : deviceManagerPrinterTab(ticket);
  const conn = es.status === 'Blocked' ? connectionPanelHtml('Device Manager', 'Printer', es) : '';
  return tabBarHtml(DEVICEMGR_TABS, tab) + conn + body;
}

function deviceManagerPrinterTab(ticket) {
  const d = ticket.desktop.Printer;
  const es = effectiveStatus(ticket.desktop, 'Printer');
  const warn = es.status !== 'OK';
  let statusText;
  if (d.connection === 'Removed') statusText = 'This device is not connected. (Device removed)';
  else if (d.driverState === 'Corrupted') statusText = 'This device cannot start. (Code 39)';
  else statusText = 'This device is working properly.';
  return `
    <div class="devmgr-tree">
      <div class="devmgr-node">▾ Print queues</div>
      <div class="devmgr-device ${warn ? 'warn' : ''}"><span class="dev-icon">🖨${warn ? ' ⚠' : ''}</span> Epson TM-T88 Receipt Printer</div>
    </div>
    <div class="devmgr-status-box">Device status: ${statusText}</div>
    <div class="app-actions">
      ${fixButtonHtml(ticket, 'reinstall_printer_driver', 'Reinstall driver')}
      ${fixButtonHtml(ticket, 'remove_readd_printer', 'Remove & re-add device (risky)')}
    </div>`;
}

function deviceManagerDevicesTab(ticket) {
  const row = (icon, name, es) =>
    `<div class="devmgr-device ${es.status !== 'OK' ? 'warn' : ''}"><span class="dev-icon">${icon}${es.status !== 'OK' ? ' ⚠' : ''}</span> ${name} — ${es.status}</div>`;
  return `
    <div class="devmgr-tree">
      <div class="devmgr-node">▾ Print queues</div>
      ${row('🖨', 'Epson TM-T88 Receipt Printer', effectiveStatus(ticket.desktop, 'Printer'))}
      <div class="devmgr-node">▾ Network adapters</div>
      ${row('📶', 'Ethernet Adapter', effectiveStatus(ticket.desktop, 'Network'))}
      <div class="devmgr-node">▾ Human Interface Devices</div>
      ${row('💵', 'Cash Drawer (HID)', effectiveStatus(ticket.desktop, 'CashDrawer'))}
      <div class="devmgr-node">▾ Point of Sale devices</div>
      ${row('💳', 'POS Terminal', effectiveStatus(ticket.desktop, 'Terminal'))}
    </div>
    <p class="hint">Only the printer entry is interactive tonight — the others are read-only.</p>`;
}

/* ---- Cash Drawer Config ---- */
const CASHDRAWER_TABS = [
  { key: 'port', label: 'Port Settings' },
  { key: 'trigger', label: 'Trigger Settings' },
];

function cashDrawerAppHtml(ticket) {
  const es = effectiveStatus(ticket.desktop, 'CashDrawer');
  const tab = ticket.appTabs.cashdrawer;
  const body = tab === 'trigger' ? cashDrawerTriggerTab(ticket) : cashDrawerPortTab(ticket);
  const conn = es.status === 'Blocked' ? connectionPanelHtml('Cash Drawer', 'CashDrawer', es) : '';
  return tabBarHtml(CASHDRAWER_TABS, tab) + conn + body;
}

function cashDrawerPortTab(ticket) {
  const d = ticket.desktop.CashDrawer;
  const conflict = effectiveStatus(ticket.desktop, 'CashDrawer').status === 'Error';
  return `
    <div class="config-row">Port: <b>${d.port}</b>${conflict ? `<span class="warn-badge">⚠ conflicts with Printer (${ticket.desktop.Printer.port})</span>` : ''}</div>
    <div class="app-actions">${fixButtonHtml(ticket, 'move_cash_drawer_port', 'Move to COM4')}</div>`;
}

function cashDrawerTriggerTab(ticket) {
  return `
    <div class="config-row">Open on: <b class="text-ok">Sale complete ✓</b></div>
    <div class="config-row">Open on: <b class="text-ok">Refund ✓</b></div>
    <div class="config-row">Manual open key: <b>F12</b></div>
    <div class="config-row">Triggered by: <b>Epson TM-T88 Receipt Printer (Port ${ticket.desktop.Printer.port})</b></div>
    <p class="hint">Trigger rules — not exercised by tonight's tickets.</p>`;
}

/* ---- Network Settings ---- */
const NETWORK_TABS = [
  { key: 'adapter', label: 'Adapter Status' },
  { key: 'details', label: 'Connection Details' },
  { key: 'impact', label: 'Downstream Impact' },
];

function networkAppHtml(ticket) {
  const tab = ticket.appTabs.network;
  let body;
  if (tab === 'details') body = networkDetailsTab(ticket);
  else if (tab === 'impact') body = networkImpactTab(ticket);
  else body = networkAdapterTab(ticket);
  return tabBarHtml(NETWORK_TABS, tab) + body;
}

function networkAdapterTab(ticket) {
  const online = ticket.desktop.Network.isOnline;
  return `
    <div class="netadapter-row"><span class="net-dot ${online ? 'online' : 'offline'}"></span>Ethernet Adapter — ${online ? 'Connected' : 'Disconnected (request timed out)'}</div>
    <div class="app-actions">${fixButtonHtml(ticket, 'reconnect_network', 'Reconnect')}</div>`;
}

function networkDetailsTab(ticket) {
  const online = ticket.desktop.Network.isOnline;
  return `
    <div class="config-row">Wi-Fi network (SSID): <b>${ticket.desktop.Network.ssid}</b></div>
    <div class="config-row">IP Address: <b>${online ? '192.168.1.42' : '—'}</b></div>
    <div class="config-row">Gateway: <b>${online ? '192.168.1.1' : '—'}</b></div>
    <div class="config-row">Last ping: <b class="${online ? 'text-ok' : 'text-err'}">${online ? 'Reply in 4ms' : 'Request timed out'}</b></div>
    <p class="hint">Every device (Terminal, Printer) must be joined to this same Wi-Fi network to reach POS — a device on a different network (guest Wi-Fi, wrong AP) won't connect even if its own internet works.</p>`;
}

function networkImpactTab(ticket) {
  const targets = [
    ['POS Software', 'POSSoftware'],
    ['Terminal', 'Terminal'],
    ['Printer', 'Printer'],
    ['Cash Drawer', 'CashDrawer'],
  ];
  const rows = targets.map(([label, mod]) => {
    const es = effectiveStatus(ticket.desktop, mod);
    return `<div class="config-row">${label}: <b class="${es.status === 'Blocked' ? 'text-err' : 'text-ok'}">${es.status}</b></div>`;
  }).join('');
  return rows + '<p class="hint">Everything downstream of Network goes Blocked the moment it drops — fix Network first before chasing other errors.</p>';
}

/* ---- POS Manager ---- */
const POS_MANAGER_TABS = [
  { key: 'receipt', label: 'Receipt Template' },
  { key: 'printer', label: 'Printer Connection' },
  { key: 'connections', label: 'Connections' },
  { key: 'staff', label: 'Staff Management' },
  { key: 'license', label: 'License & Version' },
  { key: 'database', label: 'Database' },
];

function posManagerAppHtml(ticket) {
  const es = effectiveStatus(ticket.desktop, 'POSSoftware');
  const tab = ticket.appTabs.possoftware;
  let body;
  switch (tab) {
    case 'printer': body = posManagerPrinterTab(ticket); break;
    case 'connections': body = posManagerConnectionsTab(ticket); break;
    case 'staff': body = posManagerStaffTab(ticket); break;
    case 'license': body = posManagerLicenseTab(); break;
    case 'database': body = posManagerDatabaseTab(ticket); break;
    default: body = posManagerReceiptTab(ticket); break;
  }
  const conn = es.status === 'Blocked' ? connectionPanelHtml('POS Manager', 'POSSoftware', es) : '';
  return tabBarHtml(POS_MANAGER_TABS, tab) + conn + body;
}

// The HUB's own outgoing links (GDD Mục 4: POS Software (HUB) ──► Terminal / Database / Printer).
//  - Terminal: the ACTUAL connect flow lives in POS Manager ▸ Staff Management (assign role → assign
//    terminal → sync), matching GDD Mục 15's worked example precondition chain. This tab just summarizes it.
//  - Printer: deliberately shallow per GDD ("chỉ kiểm tra 'có thấy printer không', không cấu hình
//    sâu") — no config lives here on purpose, deep config is Printer & Print Queue / Device Manager.
//  - Database: POS owns "kết nối database" (GDD) — a real, editable server-address field. It only
//    connects once the address is actually correct, matching nguyên tắc #3 (state sai → fix = đưa
//    state về đúng, not a boolean).
function posManagerConnectionsTab(ticket) {
  const posState = ticket.desktop.POSSoftware;
  const termEs = effectiveStatus(ticket.desktop, 'Terminal');
  const login = staffLoginStatus(ticket.desktop);
  const printerEs = effectiveStatus(ticket.desktop, 'Printer');
  const db = dbConnected(ticket.desktop);
  const actualIp = terminalNetInfo(ticket.desktop).ip;

  return `
    <div class="conn-panel">
      <div class="conn-row"><b>POS ↔ Terminal (REG-1)</b> — hardware/software link</div>
      <div class="conn-row">Connectivity: <b class="${termEs.status === 'OK' ? 'text-ok' : 'text-err'}">${termEs.status === 'OK' ? 'Connected' : 'Disconnected'}</b></div>
      ${termEs.status !== 'OK' ? `<div class="conn-reason">${termEs.reason}</div>` : ''}
      <div class="conn-row">Terminal's actual IP right now: <b>${actualIp}</b></div>
      <label class="field">Registered terminal IP (what POS has on file)
        <input type="text" id="pos-terminal-ip" value="${posState.registeredTerminalIp}">
      </label>
      <button class="action-btn" id="btn-terminal-ip-register">Register</button>
      <p class="hint">Must match the terminal's actual IP above to actually connect — copy it in if it's stale (e.g. after a router reboot re-assigned it).</p>
      <hr>
      <div class="conn-row">Staff login (Maria Alvarez): <b class="${login.ok ? 'text-ok' : 'text-err'}">${login.ok ? 'OK' : 'Denied'}</b></div>
      ${!login.ok ? `<div class="conn-reason">${login.reason}</div>` : ''}
      <p class="hint">Separate concern: fixed in POS Manager ▸ Staff Management, not here.</p>
    </div>

    <div class="conn-panel">
      <div class="conn-row"><b>POS ↔ Printer</b></div>
      <div class="conn-row">Printer detected: <b class="${printerEs.status !== 'Blocked' ? 'text-ok' : 'text-err'}">${printerEs.status !== 'Blocked' ? 'Yes' : 'No'}</b></div>
      <p class="hint">By design: POS only checks "is a printer visible" here, no deep config. Port/driver/spooler live in Printer &amp; Print Queue and Device Manager.</p>
    </div>

    <div class="conn-panel">
      <div class="conn-row"><b>POS ↔ Transaction Database</b></div>
      <div class="conn-row">Status: <b class="${db.ok ? 'text-ok' : 'text-err'}">${db.ok ? 'Connected' : 'Disconnected'}</b></div>
      ${!db.ok ? `<div class="conn-reason">Reason: ${db.reason}</div>` : ''}
      <label class="field">Server address
        <input type="text" id="pos-db-host" value="${posState.dbHost}">
      </label>
      <button class="action-btn" id="btn-db-connect">Connect</button>
    </div>`;
}

function posManagerPrinterTab(ticket) {
  const printer = ticket.desktop.Printer;
  const detected = printer.connection !== 'Removed';
  let html = `<div class="posmgr-row">Printer detected: <b class="${detected ? 'text-ok' : 'text-err'}">${detected ? 'Yes' : 'No'}</b></div>`;
  if (detected) {
    html += `<div class="posmgr-row">Device: <b>Epson TM-T88 Receipt Printer (Port ${printer.port})</b></div>`;
  } else {
    html += `<p class="hint">POS software cannot see any printer right now. Check Device Manager on the desktop — the device may have been removed.</p>`;
  }
  html += `<p class="hint">POS only checks whether a printer is visible — deep driver/port config lives in Device Manager and Printer &amp; Print Queue.</p>`;
  return html;
}

function posManagerReceiptTab(ticket) {
  const tplBroken = ticket.desktop.POSSoftware.receiptTemplate === 'Broken';
  let html = `<div class="posmgr-row">Connection: <b class="text-ok">OK</b></div>`;
  html += `<div class="posmgr-row">Receipt Template: <b class="${tplBroken ? 'text-err' : 'text-ok'}">${tplBroken ? 'Broken' : 'OK'}</b></div>`;
  if (tplBroken) {
    const tx = latestTransactionFor(ticket);
    const txLine = tx ? `${tx.type} .......... $${tx.amount.toFixed(2)} (${tx.status})` : 'No transaction on file';
    html += `<div class="receipt-preview">CUSTOMER COPY PREVIEW — from Transaction Database<br>--------------------------<br>${txLine}<br><b>TOTAL: [missing field]</b></div>`;
  }
  html += `<div class="app-actions">${fixButtonHtml(ticket, 'reset_pos_receipt_template', 'Reset receipt template')}</div>`;
  return html;
}

// GDD Mục 15's exact worked example ("staff mới không login được terminal"): assign role → assign
// terminal → sync POS→terminal. This governs whether THIS STAFF MEMBER can log into the terminal —
// it does NOT take down the terminal's own hardware/software connectivity (that's Network→POS only;
// per GDD, "Terminal hoàn toàn khỏe" even while one staff member's login is denied).
function posManagerStaffTab(ticket) {
  const st = ticket.desktop.POSSoftware;
  const roleOptions = ['None', 'Sale', 'Admin'].map(r =>
    `<option value="${r}" ${st.staffRole === r ? 'selected' : ''}>${r}</option>`).join('');
  const termOptions = ['', 'REG-1'].map(t =>
    `<option value="${t}" ${st.staffTerminal === t ? 'selected' : ''}>${t || '(unassigned)'}</option>`).join('');
  return `
    <table class="win-table">
      <thead><tr><th>Name</th><th>Role</th><th>Assigned Terminal</th></tr></thead>
      <tbody>
        <tr>
          <td>Maria Alvarez (Owner)</td>
          <td><select id="staff-role">${roleOptions}</select></td>
          <td><select id="staff-terminal">${termOptions}</select></td>
        </tr>
      </tbody>
    </table>
    <div class="posmgr-row">Sync status: <b class="${st.terminalSynced ? 'text-ok' : 'text-err'}">${st.terminalSynced ? 'Synced to Terminal' : 'Not synced — Terminal is running the OLD config'}</b></div>
    <div class="app-actions"><button class="action-btn" id="btn-sync-terminal">Sync POS → Terminal</button></div>
    <p class="hint">This staff member can only log into the terminal once a role AND a terminal are assigned here, AND you've synced. It only affects THIS login — the terminal's own network connection to POS is separate (see Terminal ▸ Status).</p>`;
}

function posManagerLicenseTab() {
  return `
    <div class="posmgr-row">Version: <b>POS Suite 4.2.1</b></div>
    <div class="posmgr-row">License: <b class="text-ok">Active — Sunrise Diner (1 register)</b></div>
    <div class="posmgr-row">Expires: <b>2027-03-01</b></div>`;
}

function posManagerDatabaseTab(ticket) {
  const db = dbConnected(ticket.desktop);
  if (!db.ok) {
    return `<div class="app-status-banner status-Blocked">Transaction Database: Disconnected — ${db.reason}</div>`
      + `<p class="hint">Fix this in POS Manager ▸ Connections — either the server address is wrong, or an upstream system (Network) needs fixing first.</p>`;
  }
  const days = availableDbDays(ticket);
  if (!days.includes(ticket.dbSelectedDay)) ticket.dbSelectedDay = 'Today';
  const dayOptions = days.map(d => `<option value="${d}" ${d === ticket.dbSelectedDay ? 'selected' : ''}>${d}</option>`).join('');
  const records = recordsForDay(ticket, ticket.dbSelectedDay);

  let html = `
    <div class="netadapter-row"><span class="net-dot online"></span>Transaction Database — Connected</div>
    <label class="field">View day
      <select id="db-day-picker">${dayOptions}</select>
    </label>`;

  if (!records.length) {
    html += '<p class="hint">No records for this day.</p>';
  } else {
    const rows = records.map((r, i) => {
      const printLine = r.lastPrintResult
        ? `<div style="font-size:11px;" class="${r.lastPrintResult.startsWith('PASS') ? 'text-ok' : 'text-err'}">${r.lastPrintResult}</div>`
        : '';
      const printBtns = ['Merchant Receipt', 'Customer Copy', 'Store Receipt'].map(docType =>
        `<button class="action-btn" data-db-index="${i}" data-doc-type="${docType}">${docType}</button>`
      ).join(' ');
      return `<tr><td>${r.type}${printLine}</td><td>$${r.amount.toFixed(2)}</td><td>${r.status}</td><td>${printBtns}</td></tr>`;
    }).join('');
    html += `<table class="win-table">
      <thead><tr><th>Type</th><th>Amount</th><th>Status</th><th>Print</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`;
  }
  html += `<p class="hint">Terminal only shows today's live batch — this Database view covers every day on file, and is where receipts actually get (re)printed.</p>`;
  return html;
}

/* ---- POS Terminal ---- */
const TERMINAL_TABS = [
  { key: 'status', label: 'Status' },
  { key: 'network', label: 'Network' },
  { key: 'batch', label: 'Batch' },
];

function terminalAppHtml(ticket) {
  const tab = ticket.appTabs.terminal;
  let body;
  if (tab === 'batch') body = terminalBatchTab(ticket);
  else if (tab === 'network') body = terminalNetworkTab(ticket);
  else body = terminalStatusTab(ticket);
  return tabBarHtml(TERMINAL_TABS, tab) + body;
}

// The terminal's OWN network config — like a real register's network settings screen. Picking a Wi-Fi
// is the ONLY input; IP/gateway below are read-only, always derived from that choice (real DHCP behavior:
// you don't type your own IP, the network you join hands you one from its own range).
function terminalNetworkTab(ticket) {
  const t = ticket.desktop.Terminal;
  const info = terminalNetInfo(ticket.desktop);
  const es = effectiveStatus(ticket.desktop, 'Terminal');
  const wifiOptions = NEARBY_WIFI_NETWORKS.map(ssid =>
    `<option value="${ssid}" ${t.wifiNetwork === ssid ? 'selected' : ''}>${ssid}</option>`).join('');
  return `
    <label class="field">Wi-Fi network
      <select id="terminal-wifi-select">${wifiOptions}</select>
    </label>
    <button class="action-btn" id="btn-terminal-wifi-connect">Connect</button>
    <div class="config-row" style="margin-top:10px;">IP address (assigned by DHCP): <b>${info.ip}</b></div>
    <div class="config-row">Gateway: <b>${info.gateway}</b></div>
    ${es.status !== 'OK' ? `<div class="conn-reason">${es.reason}</div>` : '<div class="conn-row"><b class="text-ok">Connected — on the right Wi-Fi and registered correctly with POS.</b></div>'}
    <p class="hint">Pick the store's actual Wi-Fi (cross-check the SSID in Network Settings ▸ Connection Details) — the IP/gateway follow automatically. If the IP still doesn't match, that's POS Manager ▸ Connections having a stale registration, not something to fix here.</p>`;
}

function terminalStatusTab(ticket) {
  // Two SEPARATE things, on purpose (GDD Mục 15): hardware/software connectivity to POS (everyone's
  // affected the same way) vs. this one staff member's login (can fail solo while Terminal is fine).
  const es = effectiveStatus(ticket.desktop, 'Terminal');
  const login = staffLoginStatus(ticket.desktop);
  let html = `<div class="app-status-banner status-${es.status}">Terminal hardware/connectivity: ${es.status}</div>`;
  if (es.status === 'Blocked') {
    html += connectionPanelHtml('Terminal', 'Terminal', es);
  } else if (es.status === 'Error') {
    html += `<div class="conn-reason">${es.reason}</div><p class="hint">Fix in Terminal ▸ Network.</p>`;
  }
  html += `<div class="posmgr-row">Login — Maria Alvarez: <b class="${login.ok ? 'text-ok' : 'text-err'}">${login.ok ? 'Logged in (Admin)' : 'Denied'}</b></div>`;
  if (!login.ok) {
    html += `<div class="conn-reason">${login.reason}</div><p class="hint">Fix in POS Manager ▸ Staff Management.</p>`;
  }
  html += '<p class="hint">Read-only status monitor — confirms whether the terminal itself is reachable, and separately, whether this staff member can log in.</p>';
  return html;
}

function terminalBatchTab(ticket) {
  const openTx = ticket.transactions.filter(t => t.status === 'Open');
  const closed = openTx.length === 0;
  const statusClass = s => (s === 'Open' ? 'text-ok' : (s === 'Voided' || s === 'Refunded') ? 'text-err' : '');
  let html = `<div class="posmgr-row">Batch #${ticket.batchId}: <b class="${closed ? 'text-err' : 'text-ok'}">${closed ? 'Closed' : 'Open'}</b></div>`;
  if (!ticket.transactions.length) {
    html += '<p class="hint">No transactions in today\'s batch yet. Reprints and older days live in POS Manager ▸ Database.</p>';
  } else {
    const rows = ticket.transactions.map((t, i) => {
      let actions = '';
      if (t.status === 'Open') actions += `<button class="action-btn" data-tx-action="void" data-tx-index="${i}">Void</button> `;
      if (t.status === 'Open' || t.status === 'Settled') actions += `<button class="action-btn" data-tx-action="refund" data-tx-index="${i}">Refund</button>`;
      return `<tr><td>${t.type}</td><td>$${t.amount.toFixed(2)}</td><td class="${statusClass(t.status)}">${t.status}</td><td>${actions}</td></tr>`;
    }).join('');
    html += `<table class="win-table">
      <thead><tr><th>Type</th><th>Amount</th><th>Status</th><th>Actions</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>
    <p class="hint">Void only works while Open. Refund works even after Settled. Printing receipts happens in POS Manager ▸ Database, not here.</p>`;
  }
  html += `<div class="app-actions"><button id="btn-close-batch" class="action-btn" ${closed ? 'disabled' : ''}>Close Batch</button></div>`;
  if (closed && ticket.transactions.length === 0) html += '<p class="hint">Batch settled and archived — see POS Manager ▸ Database.</p>';
  return html;
}

function wireAppTabs(ticket, appKey) {
  document.querySelectorAll('#app-window-body .win-tab').forEach(btn => {
    btn.addEventListener('click', () => {
      ticket.appTabs[appKey] = btn.dataset.tab;
      renderOpenApp();
    });
  });
}

function wireTerminalApp(ticket) {
  const wifiBtn = document.getElementById('btn-terminal-wifi-connect');
  if (wifiBtn) {
    wifiBtn.addEventListener('click', () => {
      const sel = document.getElementById('terminal-wifi-select');
      ticket.desktop.Terminal.wifiNetwork = sel.value;
      const info = terminalNetInfo(ticket.desktop);
      const es = effectiveStatus(ticket.desktop, 'Terminal');
      pushSessionLog(ticket, 'result', es.status === 'OK'
        ? `Connected to Wi-Fi "${sel.value}" — now at ${info.ip}.`
        : `Connected to Wi-Fi "${sel.value}" — now at ${info.ip}, but ${es.reason}`);
      renderOpenApp();
      renderTicket();
    });
  }
  const btn = document.getElementById('btn-close-batch');
  if (btn && !btn.disabled) {
    btn.addEventListener('click', () => {
      ticket.transactions.forEach(t => { if (t.status === 'Open') t.status = 'Settled'; });
      ticket.dbArchive.push(...ticket.transactions.map(t => ({ ...t, day: 'Today' })));
      ticket.transactions = [];
      ticket.batchId += 1;
      pushSessionLog(ticket, 'result', `Batch closed — settled transactions archived to the database. Terminal cleared for new batch #${ticket.batchId}.`);
      renderOpenApp();
    });
  }
  document.querySelectorAll('[data-tx-action]').forEach(txBtn => {
    txBtn.addEventListener('click', () => {
      const idx = Number(txBtn.dataset.txIndex);
      const t = ticket.transactions[idx];
      const action = txBtn.dataset.txAction;

      // Caller Authorization gate (GDD Mục 4): Refund/Void are the sensitive ops this whole mechanic
      // exists for. Confirmed authorization → proceed silently. Unconfirmed → warn before proceeding;
      // if the caller genuinely wasn't authorized, this is a real HarmEvent, not a hidden fault — it's
      // caught here, at the moment of action, same as a risky Fix action elsewhere in the game.
      if (!ticket.authorizationConfirmed) {
        const proceed = confirm(`This caller's authorization hasn't been confirmed — ${action} anyway? If they turn out not to be authorized by the owner, this counts as an unauthorized transaction.`);
        if (!proceed) return;
        if (!ticket.callerAuthorized) {
          night.harmEvents.push(`Ticket ${ticket.ticketId}: processed a ${action} without confirming caller authorization — unauthorized transaction.`);
          ticket.unauthorizedActionTaken = true;
          pushSessionLog(ticket, 'result', `${action} processed — WARNING: caller authorization was never confirmed.`);
        }
      }

      if (action === 'void' && t.status === 'Open') {
        t.status = 'Voided';
        pushSessionLog(ticket, 'result', `${t.type} $${t.amount.toFixed(2)} voided.`);
      } else if (action === 'refund') {
        t.status = 'Refunded';
        pushSessionLog(ticket, 'result', `${t.type} $${t.amount.toFixed(2)} refunded.`);
      }
      renderOpenApp();
      renderTicket();
    });
  });
}

function wireModuleApp(ticket, appKey) {
  ACTIONS.filter(a => a.app === appKey).forEach(a => {
    const btn = document.getElementById('act-' + a.id);
    if (btn && !btn.disabled) {
      btn.addEventListener('click', () => {
        runAction(ticket, a);
        renderOpenApp();
        renderTicket();
      });
    }
  });
  wireAppTabs(ticket, appKey);
  wireConnectionButtons(ticket);
  if (appKey === 'terminal') wireTerminalApp(ticket);
  if (appKey === 'possoftware' && ticket.appTabs.possoftware === 'database') wirePosManagerDatabase(ticket);
  if (appKey === 'possoftware' && ticket.appTabs.possoftware === 'connections') wirePosManagerConnections(ticket);
  if (appKey === 'possoftware' && ticket.appTabs.possoftware === 'staff') wirePosManagerStaff(ticket);
}

// The real "connect the register to POS" flow (GDD Mục 15 chain: assign role → assign terminal → sync).
// Changing role or terminal immediately un-syncs — you must explicitly Sync again, exactly like a real
// staff/terminal permission system, not a single Connect/Disconnect toggle.
function wirePosManagerStaff(ticket) {
  const roleSel = document.getElementById('staff-role');
  if (roleSel) {
    roleSel.addEventListener('change', () => {
      ticket.desktop.POSSoftware.staffRole = roleSel.value;
      ticket.desktop.POSSoftware.terminalSynced = false;
      pushSessionLog(ticket, 'result', `Staff role set to ${roleSel.value} — needs sync.`);
      renderOpenApp();
      renderTicket();
    });
  }
  const termSel = document.getElementById('staff-terminal');
  if (termSel) {
    termSel.addEventListener('change', () => {
      ticket.desktop.POSSoftware.staffTerminal = termSel.value;
      ticket.desktop.POSSoftware.terminalSynced = false;
      pushSessionLog(ticket, 'result', `Assigned terminal set to ${termSel.value || '(unassigned)'} — needs sync.`);
      renderOpenApp();
      renderTicket();
    });
  }
  const syncBtn = document.getElementById('btn-sync-terminal');
  if (syncBtn) {
    syncBtn.addEventListener('click', () => {
      const st = ticket.desktop.POSSoftware;
      if (st.staffRole !== 'None' && st.staffTerminal) {
        st.terminalSynced = true;
        pushSessionLog(ticket, 'result', 'Synced POS → Terminal.');
      } else {
        pushSessionLog(ticket, 'result', 'Sync failed — assign both a role and a terminal first.');
      }
      renderOpenApp();
      renderTicket();
    });
  }
}

function wirePosManagerConnections(ticket) {
  const ipBtn = document.getElementById('btn-terminal-ip-register');
  if (ipBtn) {
    ipBtn.addEventListener('click', () => {
      const input = document.getElementById('pos-terminal-ip');
      ticket.desktop.POSSoftware.registeredTerminalIp = input.value.trim();
      const es = effectiveStatus(ticket.desktop, 'Terminal');
      pushSessionLog(ticket, 'result', es.status === 'OK'
        ? `Registered terminal IP set to ${ticket.desktop.POSSoftware.registeredTerminalIp} — Terminal connected.`
        : `Registered terminal IP set to ${ticket.desktop.POSSoftware.registeredTerminalIp} — ${es.reason}`);
      renderOpenApp();
      renderTicket();
    });
  }
  const dbBtn = document.getElementById('btn-db-connect');
  if (dbBtn) {
    dbBtn.addEventListener('click', () => {
      const input = document.getElementById('pos-db-host');
      ticket.desktop.POSSoftware.dbHost = input.value.trim();
      const db = dbConnected(ticket.desktop);
      pushSessionLog(ticket, 'result', db.ok
        ? `Database connected (${ticket.desktop.POSSoftware.dbHost}).`
        : `Database connect failed — ${db.reason}.`);
      renderOpenApp();
      renderTicket();
    });
  }
}

function wirePosManagerDatabase(ticket) {
  const picker = document.getElementById('db-day-picker');
  if (picker) {
    picker.addEventListener('change', () => {
      ticket.dbSelectedDay = picker.value;
      renderOpenApp();
    });
  }
  document.querySelectorAll('[data-db-index]').forEach(btn => {
    btn.addEventListener('click', () => {
      const record = recordsForDay(ticket, ticket.dbSelectedDay)[Number(btn.dataset.dbIndex)];
      printReceiptFor(ticket, record, btn.dataset.docType);
      renderOpenApp();
      renderTicket();
    });
  });
}

/* ---- main ticket window render + input handlers ---- */

function renderTicket() {
  const ticket = night.active;
  renderChat(ticket);
  renderCrmSearchPanel(ticket);
  renderRemoteConnectPanel(ticket);

  // Customer already hung up — freeze the whole window (chat/CRM/remote form) so there's nothing left
  // to do except close the ticket. Only the footer's Hang Up button stays live (it's a sibling, not
  // inside .ticket-body, so it's untouched by this).
  $('.ticket-body').classList.toggle('disabled', ticket.customerHungUp);

  const statusLine = $('#tk-status-line');
  if (ticket.customerHungUp) {
    statusLine.textContent = 'Status: Customer hung up (unauthorized caller) — click Hang Up to close this ticket';
    return;
  }
  const status = evaluateTicket(ticket, ticket.desktop);
  if (status === 'Resolved') {
    statusLine.textContent = 'Status: Resolved — you can hang up cleanly now';
  } else if (status === 'Degraded') {
    statusLine.textContent = 'Status: Degraded (made worse) — hanging up now will file a complaint';
  } else {
    statusLine.textContent = 'Status: In Progress — hanging up now will count as an abandoned call';
  }
}

$('#btn-ask-symptom').addEventListener('click', () => {
  const ticket = night.active;
  const firstIssue = ISSUES[ticket.issueIds[0]];
  pushChat(ticket, 'customer', firstIssue.symptoms[0].layman);
  renderChat(ticket);
});

$('#btn-ask-storename').addEventListener('click', () => {
  const ticket = night.active;
  pushChat(ticket, 'customer', `This is ${ticket.statedStoreName}.`, { type: 'storeName', value: ticket.statedStoreName });
  renderChat(ticket);
});

$('#btn-ask-ownername').addEventListener('click', () => {
  const ticket = night.active;
  pushChat(ticket, 'customer', `My name? It's ${ticket.statedOwnerName}.`, { type: 'ownerName', value: ticket.statedOwnerName });
  renderChat(ticket);
});

$('#btn-ask-machine').addEventListener('click', () => {
  const ticket = night.active;
  pushChat(ticket, 'customer', `The machine? Uh, I think it says ${ticket.statedMachineId} on it.`, { type: 'machineId', value: ticket.statedMachineId });
  renderChat(ticket);
});

// GDD "Caller Authorization": only meaningful once the player suspects the caller isn't the owner
// (e.g. an Owner Name mismatch via compare). Answer branches on the ticket's ground truth, not a coin
// flip at click time — asking twice always gets the same answer, like a real person would give.
$('#btn-ask-authorized').addEventListener('click', () => {
  const ticket = night.active;
  if (ticket.customerHungUp) return;
  ticket.authorizationAsked = true;
  if (ticket.callerRole === 'Owner') {
    // Caller IS the owner — asking "did the owner authorize this" is nonsensical to them.
    ticket.authorizationConfirmed = true;
    pushChat(ticket, 'customer', `I'm the owner — this is my place, I don't need anyone's OK for this.`);
  } else if (ticket.callerAuthorized) {
    ticket.authorizationConfirmed = true;
    pushChat(ticket, 'customer', `Yeah, ${STORE.ownerName} told me to call about this — should be fine.`);
  } else {
    ticket.customerHungUp = true;
    pushChat(ticket, 'customer', `Uh — no, I didn't actually check with them first...`);
    pushChat(ticket, 'system', '[Call disconnected — customer hung up]');
  }
  renderTicket();
});

$('#btn-sms-receipt').addEventListener('click', () => {
  const ticket = night.active;
  const correct = Math.random() < PERSONA.honesty;
  if (correct) {
    pushChat(ticket, 'sms', `Receipt received — Store ${STORE.storeId}, Machine ${STORE.machineId}, timestamp matches tonight.`);
  } else {
    pushChat(ticket, 'sms', `Receipt received — Machine REG-2, timestamp from 3 days ago. (Doesn't line up — double check before trusting this.)`);
  }
  renderChat(ticket);
});

$('#btn-end-call').addEventListener('click', () => {
  const ticket = night.active;
  if (!ticket) return;
  if (ticket.customerHungUp) {
    // Neither a strike nor resolved credit — correctly refusing an unverified/unauthorized caller is
    // a good outcome, just not a "fixed the technical issue" one.
    finishActiveCall(ticket, 'Closed-Unauthorized');
    return;
  }
  const status = evaluateTicket(ticket, ticket.desktop);
  if (status === 'Resolved') {
    finishActiveCall(ticket, 'Closed-Resolved');
  } else if (status === 'Degraded') {
    night.harmEvents.push(`Ticket ${ticket.ticketId} closed degraded — customer complaint filed.`);
    finishActiveCall(ticket, 'Closed-Degraded');
  } else {
    if (!confirm("This issue isn't resolved yet. Hang up anyway? This counts as an abandoned call and will file a complaint.")) return;
    night.harmEvents.push(`Ticket ${ticket.ticketId} was abandoned mid-call — customer complaint filed.`);
    finishActiveCall(ticket, 'Abandoned');
  }
});

/* ===================== BOOT ===================== */

(function boot() {
  const saved = loadSave();
  if (saved && saved.campaign) {
    campaign = saved.campaign;
    CONFIG = Object.assign(CONFIG, saved.config || {});
  } else {
    campaign = defaultCampaign();
  }
  renderHub();
})();
