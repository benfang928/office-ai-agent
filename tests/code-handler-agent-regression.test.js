const fs = require('fs');
const path = require('path');
const assert = require('assert');

const sourcePath = path.join(__dirname, '..', 'ShareRibbon', 'Resources', 'js', 'code-handler.js');
const source = fs.readFileSync(sourcePath, 'utf8');

function extractFunction(name) {
  const start = source.indexOf(`function ${name}(`);
  if (start < 0) {
    throw new Error(`Function not found: ${name}`);
  }

  let braceIndex = source.indexOf('{', start);
  let depth = 0;
  let end = braceIndex;
  while (end < source.length) {
    const ch = source[end];
    if (ch === '{') depth += 1;
    if (ch === '}') {
      depth -= 1;
      if (depth === 0) {
        end += 1;
        break;
      }
    }
    end += 1;
  }

  return source.slice(start, end);
}

function loadFunction(name) {
  const fnSource = extractFunction(name);
  return eval(`(${fnSource})`);
}

global.escapeHtml = (value) => String(value)
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;')
  .replace(/'/g, '&#39;');

const getFormulaDescription = loadFunction('getFormulaDescription');
global.getFormulaDescription = getFormulaDescription;
const getStepIcon = loadFunction('getStepIcon');
global.getStepIcon = getStepIcon;
const parseJsonToPlan = loadFunction('parseJsonToPlan');
global.parseJsonToPlan = parseJsonToPlan;
const buildExecutionPlanHtml = loadFunction('buildExecutionPlanHtml');
const isJsonCommand = loadFunction('isJsonCommand');
const renderAcceptRejectButtons = loadFunction('renderAcceptRejectButtons');

global.acceptAnswer = () => {};
global.rejectAnswer = () => {};

class FakeElement {
  constructor(id = '', className = '') {
    this.id = id;
    this.className = className;
    this.children = [];
    this.style = {};
    this.dataset = {};
    this.textContent = '';
    this.onclick = null;
  }

  appendChild(child) {
    this.children.push(child);
    return child;
  }

  insertBefore(child) {
    this.children.unshift(child);
    return child;
  }

  querySelector(selector) {
    if (selector === '.accept-btn') {
      return this.children.find((child) => (child.className || '').includes('accept-btn')) || null;
    }
    if (selector === '.reject-btn') {
      return this.children.find((child) => (child.className || '').includes('reject-btn')) || null;
    }
    if (selector === '.sender-name') {
      return this.senderNameElement || null;
    }
    return null;
  }
}

function testRenderAcceptRejectButtons() {
  const chatDiv = new FakeElement('chat-u1', 'chat-container');
  chatDiv.dataset.sender = 'AI';
  const footer = new FakeElement('footer-u1', 'message-footer');

  global.document = {
    getElementById(id) {
      if (id === 'chat-u1') return chatDiv;
      if (id === 'footer-u1') return footer;
      return null;
    },
    createElement() {
      return new FakeElement();
    }
  };

  renderAcceptRejectButtons('u1');

  assert.ok(
    footer.children.some((child) => (child.className || '').includes('accept-btn')),
    'should render accept button'
  );
  assert.ok(
    footer.children.some((child) => (child.className || '').includes('reject-btn')),
    'should render reject button'
  );
}

function testMultiCommandSupport() {
  const payload = {
    commands: [
      { command: 'WriteData', params: { targetRange: 'A1:B2', data: [[1, 2], [3, 4]] } },
      { command: 'FormatRange', params: { range: 'A1:B2', style: 'header' } }
    ]
  };

  assert.strictEqual(
    isJsonCommand(JSON.stringify(payload), 'json'),
    true,
    'commands array should be treated as executable JSON'
  );

  const plan = parseJsonToPlan(payload);
  assert.ok(plan.steps.length >= 2, 'commands array should expand into executable plan steps');

  const html = buildExecutionPlanHtml(payload, 'u2', JSON.stringify(payload));
  assert.ok(html.includes('execute-plan-btn'), 'multi-command payload should render execute button');
}

testRenderAcceptRejectButtons();
testMultiCommandSupport();

console.log('code-handler agent regression tests passed');
