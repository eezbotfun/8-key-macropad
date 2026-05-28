/* 变量 */
let portOpen = false; // tracks whether a port is corrently open
let portPromise; // promise used to wait until port succesfully closed
let holdPort = null; // use this to park a SerialPort object when we change settings so that we don't need to ask the user to select it again

let port; // current SerialPort object
let reader; // current port reader object so we can call .cancel() on it to interrupt port reading
let profile = '0';
let version_number = 0;
/** How many profile tabs the device supports (from response line n=x). */
let deviceProfileCount = 5;
/** Max profile count from device (n=x); wire/protocol limit for sanity. */
const DEVICE_PROFILE_REPORT_MAX = 255;
/** Set when a line n=x is received after sending "l". Older firmware ignores "l" and never sends n=x — we keep the default 5 tabs (see scheduleProfileCountFallbackForLegacyDevices). */
let profileCountReplyReceived = false;
let profileCountFallbackTimer = null;

/** Set false to silence [webserial] console logs */
const WEBSERIAL_DEBUG = true;
function wsLog() {
  if (WEBSERIAL_DEBUG && typeof console !== 'undefined' && console.log) {
    console.log.apply(console, ['[webserial]'].concat(Array.prototype.slice.call(arguments)));
  }
}

/* 连接设备 */
document.getElementById('connectButton').addEventListener('click', () => {
  if (navigator.serial) {
    connectSerial();
  } else {
    alert('Web Serial API not supported.');
  }
});

document.getElementById('disconnectButton').addEventListener('click', async () => {
    
    portOpen = false;
    reader.cancel();
    document.getElementById('connectButton').hidden = false;
    document.getElementById('disconnectButton').hidden = true;
    resetProfileTabsToDefault();
    clearProfileCountFallbackTimer();
  
});
function to_consumer_str(number){
  let str = "{unknow}";
  switch(number){
    case 0x0030:
        str = '{POWER}';
        break;
    case 0x0031:
        str = '{RESET}';
        break;
    case 0x0032:
        str = '{SLEEP}';
        break;

    case 0x00CD:
        str = '{PLAY}';
        break;
    case 0x00B5:
        str = '{NEXT}';
        break;
    case 0x00B6:
        str = '{PREV}';
        break;
    case 0x00B7:
        str = '{STOP}';
        break;
    
    case 0x00E2:
        str = '{MUTE}';
        break;
    case 0x00E9:
        str = '{VOL_INC}';
        break;
    case 0x00EA:
        str = '{VOL_DEC}';
        break;

    case 0x0183:
        str = '{CONFIG}';
        break;
    case 0x018A:
        str = '{EMAIL}';
        break;
    case 0x0192:
        str = '{CALC}';
        break;
    case 0x0194:
        str = '{BROWSER}';
        break;

    case 0x0221:
        str = '{SEARCH}';
        break;
    case 0x0223:
        str = '{HOME}';
        break;
    case 0x0224:
        str = '{BACK}';
        break;
    case 0x0225:
        str = '{FORWARD}';
        break;
    case 0x0227:
        str = '{REFRESH}';
        break;
    case 0x022A:
        str = '{BOOKMARKS}';
        break;    

    default:
      break;

  }
  return str;
}

function handle_line(raw){
  let line = typeof raw === 'string' ? raw.trim() : '';
  if (!line.length) return;
  wsLog('line in:', JSON.stringify(line), 'len=', line.length);
  let content = line.substring(2);
  let cmd = line.charAt(0);
  if (cmd === 'n' && /^n=\d+/.test(line)) {
    const m = line.match(/^n=(\d+)/);
    if (m) {
      wsLog('matched profile count n=', m[1]);
      onDeviceProfileCountLineReceived();
      updateProfileTabsForDeviceCount(parseInt(m[1], 10));
    } else {
      wsLog('n= line but regex failed:', line);
    }
    return;
  }
  switch(cmd){
      case 'v':
        wsLog('version / v line (ignored by UI)');
        break;
      case 'a':
        document.getElementById("checkbox_alias").checked = true;
        document.getElementById('aliasconainer').hidden = false;
        document.querySelector(".alias_input").value = content;
        break;
      case 'b':
        document.getElementById("checkbox_alias").checked = false;
        document.getElementById('aliasconainer').hidden = true;
        document.querySelector(".alias_input").value = '';
        break;

      case 'c':
        document.querySelector(".input").value = content;
        keyboard.setInput(content);
        break;

      default:
        wsLog('unhandled line (cmd=', cmd, '):', JSON.stringify(line));
        break;
  }
}
async function connectSerial() {
  const log = document.getElementById('device_rsp');
  log.textContent = "connecting" ;
  wsLog('connectSerial: start');
  try {
    const filter = { usbVendorId: 0x303a };
    port = await navigator.serial.requestPort({ filters: [filter] });
    
    await port.open({ baudRate: 115200 });
    
    const textDecoder = new TextDecoderStream();
    const readableStreamClosed = port.readable.pipeTo(textDecoder.writable);
     reader = textDecoder.readable.getReader();

    // conceal connect button, display disconnect button instead
    document.getElementById('connectButton').hidden = true;
    document.getElementById('disconnectButton').hidden = false;

    portOpen = true;
    log.textContent = "Connected" ;
    wsLog('port open, portOpen=', portOpen);

    /* Profile stays '0' until a tab is clicked; users often skip Step 2 and Save appears to do nothing. */
    applyProfileUI('1', document.querySelector('.tab .tablinks'), false);
    updateProfileActionButtonsEnabled();

    wsLog('sending version query…');
    await sendQuereVersion();
    wsLog('sending profile-count query (l)…');
    await sendProfileCountQuery();
    scheduleProfileCountFallbackForLegacyDevices();
    let rsp ="";

    while (portOpen && port.readable) {
      const { value, done } = await reader.read();
      if (value) {
        rsp += value;
        wsLog('read chunk, buffer length=', rsp.length, 'last chars=', JSON.stringify(rsp.slice(-120)));
        while (true) {
          var idx = rsp.indexOf('\r\n');
          var sepLen = 2;
          if (idx < 0) {
            idx = rsp.indexOf('\n');
            sepLen = 1;
          }
          if (idx < 0) break;
          var line = rsp.substring(0, idx);
          rsp = rsp.substring(idx + sepLen);
          handle_line(line);
        }
      }
      if (done) {
        console.log('[readLoop] DONE', done);
        reader.releaseLock();
        break;
      }
    }
    
    console.log("disconnecting ... ");
    //const textEncoder = new TextEncoderStream();
    //const writableStreamClosed = textEncoder.readable.pipeTo(port.writable);

    //reader.cancel();
    await readableStreamClosed.catch(() => { /* Ignore the error */ });
    
    //writer = port.writable.getWriter();
    //writer.close();
    //await writableStreamClosed;

    await port.close();
    console.log("port closed");
} catch (error) {
    wsLog('connectSerial error:', error);
    log.innerHTML = error;
    alert("Please make sure the configurator app is not running. Since the macro pad cannot simultaneously connect to both web configurator and app configurator.");
  }
}

/* 保存配置 — await async sendConfig so errors surface; avoid racing the serial writer */
(function wireSaveConfigButton() {
  const saveBtn = document.getElementById('saveConfigButton');
  if (!saveBtn) {
    console.error('saveConfigButton missing from DOM');
    return;
  }
  saveBtn.type = 'button';
  saveBtn.addEventListener('click', async () => {
    if (!navigator.serial) {
      alert('Web Serial API not supported.');
      return;
    }
    if (!portOpen) {
      alert('Please connect the device first.');
      return;
    }
    saveBtn.disabled = true;
    try {
      await sendConfig();
    } catch (err) {
      console.error(err);
      const msg = err && err.message ? err.message : String(err);
      const log = document.getElementById('device_rsp');
      if (log) log.textContent = 'Save failed: ' + msg;
      alert('Save failed: ' + msg);
    } finally {
      saveBtn.disabled = false;
    }
  });
})();

(function wireAddProfileTabButton() {
  var addBtn = document.getElementById('addProfileTabButton');
  if (!addBtn) return;
  addBtn.addEventListener('click', function () {
    addProfileTabAndNotifyDevice().catch(function (err) {
      console.error(err);
    });
  });
})();

(function wireRemoveProfileTabButton() {
  var remBtn = document.getElementById('removeProfileTabButton');
  if (!remBtn) return;
  remBtn.addEventListener('click', function () {
    deleteLastProfileAndNotifyDevice().catch(function (err) {
      console.error(err);
    });
  });
})();
updateProfileActionButtonsEnabled();

/** PAYLOAD_LEN on the wire is exactly one byte; value = COMMAND length in bytes, range 0..255 inclusive. */
const EBF_PAYLOAD_LEN_MAX = 255;

function assertEbfCommandByteLength(commandByteLength, context) {
  if (commandByteLength < 0 || commandByteLength > EBF_PAYLOAD_LEN_MAX) {
    const msg = (context || 'ebf') + ': COMMAND byte length ' + commandByteLength + ' is out of PAYLOAD_LEN range 0..' + EBF_PAYLOAD_LEN_MAX;
    console.error(msg);
    throw new Error(msg);
  }
}

/**
 * On-wire layout: [MAGIC_STR][PAYLOAD_LEN][COMMAND]
 * MAGIC_STR = "ebf" (3 bytes). PAYLOAD_LEN = fixed 1 byte, 0..255 = byte length of COMMAND.
 * COMMAND = bytes from index 4 to end of frame.
 * input_str is built as "ebf" + placeholder + COMMAND; byte at index 3 is overwritten with PAYLOAD_LEN.
 */
function toU8Array(input_str){
  let len = input_str.length;
  const commandByteLength = len - 4;
  assertEbfCommandByteLength(commandByteLength, 'toU8Array');
  let u8Array = new Uint8Array(len);

  for (let i = 0; i < len; i++) {
    u8Array[i] = input_str.charCodeAt(i);
  }

  u8Array[3] = commandByteLength;
  return u8Array;
}

/**
 * Build a full ebf frame where COMMAND is an ASCII/Latin1 string (one char = one byte).
 * PAYLOAD_LEN is one byte, so COMMAND length must be 0..255 (enforced in toU8Array).
 */
function ebfFrameFromCommand(commandStr) {
  return toU8Array('ebf' + '0' + commandStr);
}
/* 发送按键定义配置到设备端 */
// Send a string over the serial port.
// This is easier than listening because we know when we're done sending
async function sendConfig() {
  let input = document.querySelector(".input").value// get the string to send from the term_input textarea

  if (!is_valid(input)){
      alert("Invalid Input.");
      return;
  }
  if ( '0' == profile ){
    alert("Please select a profile (layout) in Step 2 by clicking 1–5 above.");
    return;
  }

  console.log("input : ",input);
  let encoded = encode(input);
  if (encoded === '') {
    alert('Could not build config (e.g. invalid media key combination).');
    return;
  }
  console.log("sending encoded : ",encoded);
  writer = port.writable.getWriter();
  try {
    await writer.write(toU8Array(encoded));

    let enable =  !document.getElementById('aliasconainer').hidden;
    let alias = document.querySelector(".alias_input").value;
    let encodedAliasConfig = encodeAliasConfig(enable, alias);
    await writer.write((encodedAliasConfig));

    enable =  !document.getElementById('scriptconainer').hidden;
    let script = document.querySelector(".script_input").value;
    let encodedScriptConfig = encodeScriptConfig(enable, script);
    await writer.write(toU8Array(encodedScriptConfig));
  } finally {
    writer.releaseLock();
  }
  await sendEnableHidMode();
}
/* 发送按键别名配置到设备端 */
async function sendAliasConfig(enable) {
  let input = document.querySelector(".alias_input").value
  console.log("alias_input : ",input);
  let encoded = encodeAliasConfig(enable, input);

  writer = port.writable.getWriter();

  // write the encoded to the writer  
  await writer.write(encoded);

  // close the writer since we're done sending for now
  writer.releaseLock();
}

/* 发送按键别名配置到设备端 */
async function sendWifiConfig() {
  let ssid = document.querySelector(".ssid_input").value
  let password = document.querySelector(".password_input").value
  //console.log("wifi : ",ssid +" / " + password);
  let encoded = encodeWifiConfig( ssid,password);
  //console.log("encoded : ",encoded);
  let u8Array = toU8Array(encoded)


  writer = port.writable.getWriter();
  await writer.write(u8Array);
  writer.releaseLock();
}

/* Enable HID mode — COMMAND = a{profile_id}.{key_id} e.g. a3.1 (ebf frame) */
async function sendEnableHidMode() {
  if (!portOpen || profile === '0') {
    return;
  }
  const cmd = 'a' + profile + '.' + top.keyNumber;
  if (cmd.includes('undefined')) {
    console.log('sendEnableHidMode: invalid cmd');
    return;
  }
  wsLog('sendEnableHidMode: ebf frame COMMAND=', JSON.stringify(cmd));
  const u8Array = ebfFrameFromCommand(cmd);
  writer = port.writable.getWriter();
  try {
    await writer.write(u8Array);
  } finally {
    writer.releaseLock();
  }
}

/* 发送获取设备版本号 — COMMAND byte '5' */
async function sendQuereVersion() {
  wsLog('sendQuereVersion: ebf frame COMMAND=', JSON.stringify('5'));
  const u8Array = ebfFrameFromCommand('5');
  writer = port.writable.getWriter();
  await writer.write(u8Array);
  writer.releaseLock();
}

/**
 * Ask device how many profiles exist; COMMAND is 'l'. Device may reply with line n=x.
 * Older firmware: no n=x (see scheduleProfileCountFallbackForLegacyDevices).
 */
async function sendProfileCountQuery() {
  const u8Array = ebfFrameFromCommand('l');
  wsLog('sendProfileCountQuery: ebf frame bytes', Array.from(u8Array));
  writer = port.writable.getWriter();
  try {
    await writer.write(u8Array);
    wsLog('sendProfileCountQuery: write done');
  } finally {
    writer.releaseLock();
  }
}

/** Tell device total profile count: COMMAND = "m" + count as decimal string (ebf framing). */
async function sendDeviceProfileCountCommand(totalProfiles) {
  var cmd = 'm' + String(totalProfiles);
  wsLog('sendDeviceProfileCountCommand:', JSON.stringify(cmd));
  const u8Array = ebfFrameFromCommand(cmd);
  writer = port.writable.getWriter();
  try {
    await writer.write(u8Array);
    wsLog('sendDeviceProfileCountCommand: write done');
  } finally {
    writer.releaseLock();
  }
}

function clearProfileCountFallbackTimer() {
  if (profileCountFallbackTimer) {
    wsLog('clearProfileCountFallbackTimer');
    clearTimeout(profileCountFallbackTimer);
    profileCountFallbackTimer = null;
  }
}

function onDeviceProfileCountLineReceived() {
  wsLog('onDeviceProfileCountLineReceived: got n=x');
  profileCountReplyReceived = true;
  clearProfileCountFallbackTimer();
}

/** If no n=x arrives after "l", assume older firmware and leave all 5 profile tabs visible. */
function scheduleProfileCountFallbackForLegacyDevices() {
  clearProfileCountFallbackTimer();
  profileCountReplyReceived = false;
  wsLog('scheduleProfileCountFallbackForLegacyDevices: 2.5s fallback if no n=x');
  profileCountFallbackTimer = setTimeout(function () {
    profileCountFallbackTimer = null;
    if (!portOpen || profileCountReplyReceived) {
      wsLog('legacy fallback skipped (portOpen=', portOpen, 'profileCountReplyReceived=', profileCountReplyReceived, ')');
      return;
    }
    wsLog('legacy fallback: no n=x, restoring 5 profile tabs');
    resetProfileTabsToDefault();
    var cur = parseInt(profile, 10);
    if (!Number.isFinite(cur) || cur < 1) cur = 1;
    cur = Math.min(cur, 5);
    var tabBtns = document.querySelectorAll('.tab .tablinks');
    if (tabBtns[cur - 1]) {
      applyProfileUI(String(cur), tabBtns[cur - 1], false);
    }
  }, 2500);
}

function getProfileTabBar() {
  return document.querySelector('.tab');
}

function getProfileTabCount() {
  var bar = getProfileTabBar();
  return bar ? bar.querySelectorAll('.tablinks').length : 0;
}

/** Create tab button + tabcontent for profiles (existingCount+1)..targetCount. Inserts content before #kb1. */
function ensureProfileSlotsThrough(targetCount) {
  var bar = getProfileTabBar();
  if (!bar || !Number.isFinite(targetCount) || targetCount < 1) return;
  var existing = bar.querySelectorAll('.tablinks').length;
  var keyboardRow = document.getElementById('kb1');
  var parent = keyboardRow && keyboardRow.parentNode;
  var insertBeforeRef = document.getElementById('addProfileTabButton')
    || document.getElementById('removeProfileTabButton');
  for (var num = existing + 1; num <= targetCount; num++) {
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'tablinks';
    btn.textContent = String(num);
    btn.setAttribute('onclick', "setProfile(event, '" + num + "')");
    if (insertBeforeRef && insertBeforeRef.parentNode === bar) {
      bar.insertBefore(btn, insertBeforeRef);
    } else {
      bar.appendChild(btn);
    }
    var content = document.createElement('div');
    content.id = String(num);
    content.className = 'tabcontent';
    if (document.querySelector('[id="1"] label')) {
      var lab = document.createElement('label');
      lab.textContent = '您正在配置键盘布局 ' + num;
      content.appendChild(lab);
    } else {
      var h3 = document.createElement('h3');
      h3.textContent = 'You are configuring Profile ' + num;
      content.appendChild(h3);
    }
    if (keyboardRow && parent) {
      parent.insertBefore(content, keyboardRow);
    } else {
      bar.parentNode.insertBefore(content, bar.nextSibling);
    }
  }
}

/** Remove profile UI slots with index > maxKeep (dynamic slots only; base 1..5 stay in HTML). */
function pruneProfileSlotsAbove(maxKeep) {
  var bar = getProfileTabBar();
  if (!bar) return;
  var btns = Array.from(bar.querySelectorAll('.tablinks'));
  btns.forEach(function (btn) {
    var n = parseInt(btn.textContent.trim(), 10);
    if (Number.isFinite(n) && n > maxKeep) {
      btn.remove();
      var tc = document.getElementById(String(n));
      if (tc && tc.classList.contains('tabcontent')) {
        tc.remove();
      }
    }
  });
}

function updateProfileActionButtonsEnabled() {
  var n = getProfileTabCount();
  var rem = document.getElementById('removeProfileTabButton');
  var add = document.getElementById('addProfileTabButton');
  if (rem) rem.disabled = n <= 1;
  if (add) add.disabled = n >= DEVICE_PROFILE_REPORT_MAX;
}

function resetProfileTabsToDefault() {
  wsLog('resetProfileTabsToDefault: deviceProfileCount=5, prune slots > 5');
  deviceProfileCount = 5;
  pruneProfileSlotsAbove(5);
  ensureProfileSlotsThrough(5);
  var tabBtns = document.querySelectorAll('.tab .tablinks');
  var i;
  for (i = 0; i < tabBtns.length; i++) {
    tabBtns[i].style.display = '';
    tabBtns[i].hidden = false;
  }
  updateProfileActionButtonsEnabled();
}

function updateProfileTabsForDeviceCount(rawCount) {
  var n = parseInt(rawCount, 10);
  if (!Number.isFinite(n) || n < 1) n = 1;
  if (n > DEVICE_PROFILE_REPORT_MAX) n = DEVICE_PROFILE_REPORT_MAX;
  wsLog('updateProfileTabsForDeviceCount: raw=', rawCount, '→ using n=', n);
  ensureProfileSlotsThrough(n);
  deviceProfileCount = n;
  var tabBtns = document.querySelectorAll('.tab .tablinks');
  var i;
  for (i = 0; i < tabBtns.length; i++) {
    var num = i + 1;
    if (num > n) {
      tabBtns[i].style.display = 'none';
      tabBtns[i].hidden = true;
    } else {
      tabBtns[i].style.display = '';
      tabBtns[i].hidden = false;
    }
  }
  document.querySelectorAll('.tabcontent').forEach(function (tc) {
    var tid = parseInt(tc.id, 10);
    if (!Number.isFinite(tid)) return;
    if (tid > n) tc.style.display = 'none';
  });
  var cur = parseInt(profile, 10);
  if (!Number.isFinite(cur)) cur = 1;
  if (cur > n) {
    applyProfileUI(String(n), tabBtns[n - 1], true);
  } else {
    applyProfileUI(String(cur), tabBtns[cur - 1], false);
  }
  updateProfileActionButtonsEnabled();
}

async function deleteLastProfileAndNotifyDevice() {
  if (!portOpen) {
    alert('Please connect the device first.');
    return;
  }
  var n = getProfileTabCount();
  if (n <= 1) {
    alert('At least one profile must remain.');
    return;
  }
  var newN = n - 1;
  if (!window.confirm('Remove profile ' + n + '? Total profiles will become ' + newN + ' on this page and the device.')) {
    return;
  }
  pruneProfileSlotsAbove(newN);
  deviceProfileCount = newN;
  var tabBtns = document.querySelectorAll('.tab .tablinks');
  var i;
  for (i = 0; i < tabBtns.length; i++) {
    tabBtns[i].hidden = false;
    tabBtns[i].style.display = '';
  }
  document.querySelectorAll('.tabcontent').forEach(function (tc) {
    var tid = parseInt(tc.id, 10);
    if (!Number.isFinite(tid)) return;
    if (tid > newN) {
      tc.style.display = 'none';
    }
  });
  var cur = parseInt(profile, 10);
  if (!Number.isFinite(cur)) cur = 1;
  try {
    await sendDeviceProfileCountCommand(newN);
  } catch (err) {
    console.error(err);
    alert('Failed to notify device: ' + (err.message ? err.message : String(err)));
  }
  tabBtns = document.querySelectorAll('.tab .tablinks');
  if (cur > newN) {
    applyProfileUI(String(newN), tabBtns[newN - 1], true);
  } else {
    applyProfileUI(String(cur), tabBtns[cur - 1], true);
  }
  updateProfileActionButtonsEnabled();
}

async function addProfileTabAndNotifyDevice() {
  if (!portOpen) {
    alert('Please connect the device first.');
    return;
  }
  var n = getProfileTabCount();
  if (n >= DEVICE_PROFILE_REPORT_MAX) {
    alert('Maximum profile count reached (' + DEVICE_PROFILE_REPORT_MAX + ').');
    return;
  }
  var newN = n + 1;
  ensureProfileSlotsThrough(newN);
  deviceProfileCount = newN;
  var tabBtns = document.querySelectorAll('.tab .tablinks');
  var i;
  for (i = 0; i < tabBtns.length; i++) {
    var num = i + 1;
    tabBtns[i].hidden = num > newN;
    tabBtns[i].style.display = num > newN ? 'none' : '';
  }
  document.querySelectorAll('.tabcontent').forEach(function (tc) {
    var tid = parseInt(tc.id, 10);
    if (!Number.isFinite(tid)) return;
    if (tid > newN) {
      tc.style.display = 'none';
    }
  });
  try {
    await sendDeviceProfileCountCommand(newN);
  } catch (err) {
    console.error(err);
    alert('Failed to notify device: ' + (err.message ? err.message : String(err)));
  }
  tabBtns = document.querySelectorAll('.tab .tablinks');
  applyProfileUI(String(newN), tabBtns[newN - 1], true);
  updateProfileActionButtonsEnabled();
}

/* 发送获取配置 */
async function sendQuereCfg() {
  if ( !portOpen || '0' == profile ){
    return;
  }
  
  /* 首字节插入 "ebf"type , 共4个字节 */
  /* 形如：[MAGIC_STR][TYPE][KEY_NUM]:CONFIG 
  如：ebf01.1:复制 (MAGIC_STR=ebf, KEY_NUM=1)*/
  const cmd = '8' + profile + '.' + top.keyNumber;
  if (cmd.includes("undefined")){
    console.log("has undefined");
    return;
  }
  const u8Array = ebfFrameFromCommand(cmd);

  writer = port.writable.getWriter();
  await writer.write(u8Array);
  writer.releaseLock();
  //console.log("done");
}

/* 转换成设备可识别配置字符串 */
function encodeLedConfig(color,brightness){

  /* 首字节插入 "ebf"type , 共4个字节 */
  /* 形如：[MAGIC_STR][TYPE 1BYTE]:[color 2BYTE][brightness 1BYTE] 
  如：ebf1:1123ab */
  //let encoded = "ebf1:" + color + brightness; 
  let encoded = "ebf";//type = 1 LED COLOR配置
  //去掉#
  encoded += '0'; //payload len
  encoded += '1';//type = 1 LED COLOR配置
  encoded += color.substring(1) ;
  encoded += brightness;

  return encoded;
}
async function sendLedConfig(){
  let colorValue = document.getElementById('ledcolor').value;
  let brightnessValue = document.getElementById('ledbrightness').value;
  //console.log('color = '+colorValue);
  let encoded = encodeLedConfig(colorValue, brightnessValue);
  //console.log("sendLedConfig : ",encoded);

  let u8Array = toU8Array(encoded)


  writer = port.writable.getWriter();
  await writer.write(u8Array);
  writer.releaseLock();
  await sendEnableHidMode();
}

document.getElementById('saveLedConfigButton').addEventListener('click', () => {
  if (navigator.serial) {
    if (portOpen){
      sendLedConfig();
    }
    else{
      alert('Please connect the device first.');
    }
    
  } else {
    alert('Web Serial API not supported.');
  }
});

document.getElementById('updateFirmwareButton').addEventListener('click', () => {
  if (navigator.serial) {
    if (portOpen){
      sendWifiConfig();
    }
    else{
      alert('Please connect the device first.');
    }
    
  } else {
    alert('Web Serial API not supported.');
  }
});

function getModifierNum(input){
   let modifierNum = 0;
   modifierNum += input.includes("{lctrl}")?1:0;
   modifierNum += input.includes("{lshift}")?1:0;
   modifierNum += input.includes("{lalt}")?1:0;
   modifierNum += input.includes("{lgui}")?1:0;
   modifierNum += input.includes("{rctrl}")?1:0;
   modifierNum += input.includes("{rshift}")?1:0;
   modifierNum += input.includes("{ralt}")?1:0;
   modifierNum += input.includes("{rgui}")?1:0;
   return modifierNum;
}
/* 校验输入 */
function is_valid(input){
   let replaced = replaceModifier(input);
   let modifierNum = getModifierNum(input);

   let len = replaced.length;
   //console.log("replaced ="+replaced+"modifierNum = "+modifierNum);
   //console.log("num of chars = "+len);
   if (modifierNum > 0){
        if ( len - modifierNum > 6 ){
            alert("only 6 chars allowed when using key modifier, such as {ctrl}");
            return false;
        }
        else{
            return true;
        }
   }
   else{
        /* 最多字符个数 */
        if ( len  > 128 ){
          alert("too many chars, only 64 chars allowed");
          return false;
      }
      else{
          return true;
      }
   }
}

function replaceModifier(input){
  let placed = input.replace("{lctrl}","\xe0");
  placed = placed.replace("{lshift}","\xe1");
  placed = placed.replace("{lalt}","\xe2");
  placed = placed.replace("{lgui}","\xe3");
  placed = placed.replace("{rctrl}","\xe4");
  placed = placed.replace("{rshift}","\xe5");
  placed = placed.replace("{ralt}","\xe6");
  placed = placed.replace("{rgui}","\xe7");

  
  
  placed = placed.replace("{arrowup}","\xd2");
  placed = placed.replace("{arrowdown}","\xd1");
  placed = placed.replace("{arrowleft}","\xd0");
  placed = placed.replace("{arrowright}","\xcf");
  placed = placed.replace("{pagedown}","\xce");
  placed = placed.replace("{end}","\xcd");
  placed = placed.replace("{delete}","\xcc");
  placed = placed.replace("{pageup}","\xcb");
  placed = placed.replace("{home}","\xca");
  placed = placed.replace("{insert}","\xc9");
  placed = placed.replace("{pause}","\xc8");
  placed = placed.replace("{scrolllock}","\xc7");
  placed = placed.replace("{prtscr}","\xc6");
  placed = placed.replace("{f12}","\xc5");
  placed = placed.replace("{f11}","\xc4");
  placed = placed.replace("{f10}","\xc3");
  placed = placed.replace("{f9}","\xc2");
  placed = placed.replace("{f8}","\xc1");
  placed = placed.replace("{f7}","\xc0");
  placed = placed.replace("{f6}","\xbf");
  placed = placed.replace("{f5}","\xbe");
  placed = placed.replace("{f4}","\xbd");
  placed = placed.replace("{f3}","\xbc");
  placed = placed.replace("{f2}","\xbb");
  placed = placed.replace("{f1}","\xba");

  placed = placed.replace("{enter}","\xa8");
  placed = placed.replace("{escape}","\xa9");
  placed = placed.replace("{bkspace}","\xaa");
  placed = placed.replace("{tab}","\xaB");
  placed = placed.replace("{space}","\xac");
  
  //console.log("replaced=",ascii_to_hex(placed));
  return placed;
}
function replaceConsumerCtrl(input){
  let placed = input.replace("{power}","\x00\x30");
  placed = placed.replace("{reset}","\x00\x31");
  placed = placed.replace("{sleep}","\x00\x32");

  placed = placed.replace("{play_pause}","\x00\xcd");
  placed = placed.replace("{next}","\x00\xb5");
  placed = placed.replace("{prev}","\x00\xb6");
  placed = placed.replace("{stop_play}","\x00\xb7");
  placed = placed.replace("{volume}","\x00\xe0");
  placed = placed.replace("{mute}","\x00\xe2");
  placed = placed.replace("{vol_inc}","\x00\xe9");
  placed = placed.replace("{vol_dec}","\x00\xea");

  placed = placed.replace("{config}","\x01\x83");
  placed = placed.replace("{email}","\x01\x8a");
  placed = placed.replace("{calc}","\x01\x92");
  placed = placed.replace("{browser}","\x01\x94");

  placed = placed.replace("{web_search}","\x02\x21");
  placed = placed.replace("{web_home}","\x02\x23");
  placed = placed.replace("{web_back}","\x02\x24");
  placed = placed.replace("{web_forward}","\x02\x25");
  placed = placed.replace("{web_stop}","\x02\x26");
  placed = placed.replace("{web_refresh}","\x02\x27");
  placed = placed.replace("{web_bkmarks}","\x02\x2a");

  //console.log("replaced=",ascii_to_hex(placed));
  return placed;
}
function getConsumerCtrlNum(input){
   let num = 0;
   num += input.includes("{power}")?1:0;
   num += input.includes("{reset}")?1:0;
   num += input.includes("{sleep}")?1:0;

   num += input.includes("{play_pause}")?1:0;
   num += input.includes("{next}")?1:0;
   num += input.includes("{prev}")?1:0;
   num += input.includes("{stop_play}")?1:0;
   num += input.includes("{volume}")?1:0;
   num += input.includes("{mute}")?1:0;
   num += input.includes("{vol_inc}")?1:0;
   num += input.includes("{vol_dec}")?1:0;

   num += input.includes("{config}")?1:0;
   num += input.includes("{email}")?1:0;
   num += input.includes("{calc}")?1:0;
   num += input.includes("{browser}")?1:0;

   num += input.includes("{web_search}")?1:0;
   num += input.includes("{web_home}")?1:0;
   num += input.includes("{web_back}")?1:0;
   num += input.includes("{web_forward}")?1:0;
   num += input.includes("{web_stop}")?1:0;
   num += input.includes("{web_refresh}")?1:0;
   num += input.includes("{web_bkmarks}")?1:0;
   return num;
}

/* 转换成设备可识别配置字符串 */
function encode(input){
  let replaced;
  let modifierNum = getModifierNum(input);
  let consumerCtrlNum = getConsumerCtrlNum(input);

  if ( consumerCtrlNum ){
    if (consumerCtrlNum > 1){
      alert("Media or consumer keys can only be used alone.");
      return "";
    }
    replaced = replaceConsumerCtrl(input);
  }
  else{
     
    //console.log("encoding ... modifierNum = ",modifierNum);
    replaced = replaceModifier(input);
  }
  

  /* 首字节插入 "ebf"type , 共4个字节 */
  /* 形如：[MAGIC_STR][PAYLOAD_LEN][TYPE][PROFILE].[KEY_NUM]:CONFIG 
  如：ebfb01.1:1123abc (MAGIC_STR=ebf,PAYLOAD_LEN=0xb(11) )*/
  let encoded = "ebf";  
  encoded += '0';//PAYLOAD_LEN
  encoded += '0';//type = 0 键盘配置
  encoded += profile;
  encoded += ".";
  encoded += top.keyNumber;
  encoded += ":";
  encoded += consumerCtrlNum==1?'2':(modifierNum>0?'1':'0');
  encoded += replaced; 

  //console.log("encoded = ",encoded);
  //let len = encoded.length - 4;
  //console.log(" len = ",len);
  //let len_in_hex = len.toString(16);
  //console.log(" len_in_hex = ",len_in_hex);
  return encoded;
}
/* 转换成设备可识别配置字符串 */
function encodeAliasConfig(enable,input){

  console.log("encodeAliasConfig : input = "+ input + " len = " + input.length);

  var alias = new TextEncoder("utf-8").encode(input);
  //console.log(alias ); 
  
  let alias_len = alias.length;
  let alias_array = new Uint8Array(alias_len);
  for (let i = 0; i < alias_len; i++) {
    alias_array[i] = alias[i];
  }
  //console.log("alias_array="+alias_array ); 
  
  /* 首字节插入 "ebf"type , 共4个字节 */
  /* 形如：[MAGIC_STR][TYPE][KEY_NUM]:CONFIG 
  如：ebf01.1:复制 (MAGIC_STR=ebf, KEY_NUM=1)*/
  let encoded = "ebf";  

  if ( enable ){
    encoded += '0';//PAYLOAD_LEN
    encoded += '2';//type = 2 增加按键别名配置
    encoded += profile;
    encoded += ".";
    encoded += top.keyNumber;
    encoded += ":";
  }
  else{
    encoded += '0';//PAYLOAD_LEN
    encoded += '3';//type = 3 删除按键别名配置
    encoded += profile;
    encoded += ".";
    encoded += top.keyNumber;
    encoded += ":";
  }

  let len = encoded.length;
  let u8Array = new Uint8Array(len);
  for (let i = 0; i < len; i++) {
    u8Array[i] = encoded.charCodeAt(i);
  }
  //console.log("u8Array = ",u8Array);
  
  if ( enable ){
    const aliasCmdBytes = len - 4 + alias_array.length;
    assertEbfCommandByteLength(aliasCmdBytes, 'encodeAliasConfig(enable)');
    u8Array[3] = aliasCmdBytes;
    let concatArray = new Uint8Array( u8Array.length + alias_array.length );
    concatArray.set(u8Array);
    concatArray.set(alias_array,u8Array.length);
    //console.log("concat = ",concatArray);
    return concatArray;
  }
  else{
    assertEbfCommandByteLength(len - 4, 'encodeAliasConfig(disable)');
    u8Array[3] = len - 4;
    return u8Array;     
  }
  
  
  

  return u8Array;
}

function encodeScriptConfig(enable,input){

  //console.log("encodeScriptConfig : input = "+ input + " len = " + input.length);

  /* 首字节插入 "ebf"type , 共4个字节 */
  /* 形如：[MAGIC_STR][TYPE][KEY_NUM]:CONFIG 
  如：ebf01.1:复制 (MAGIC_STR=ebf, KEY_NUM=1)*/
  let encoded = "ebf";  

  if ( enable ){
    encoded += '0';//PAYLOAD_LEN
    encoded += '6';//type = 6 增加按键脚本配置
    encoded += profile;
    encoded += ".";
    encoded += top.keyNumber;
    encoded += ":";
    encoded += input;
  }
  else{
    encoded += '0';//PAYLOAD_LEN
    encoded += '7';//type = 7 删除按键脚本配置
    encoded += profile;
    encoded += ".";
    encoded += top.keyNumber;
    encoded += ":";
  }
  

  return encoded;
}

function encodeWifiConfig(ssid,password){


  /* 首字节插入 "ebf"type , 共4个字节 */
  /* 形如：[MAGIC_STR][TYPE][KEY_NUM]:CONFIG 
  如：ebf01.1:复制 (MAGIC_STR=ebf, KEY_NUM=1)*/
  let encoded = "ebf";  

    encoded += '0';//PAYLOAD_LEN
    encoded += '4';//type = 4 wifi配置
    encoded += ssid;
    encoded += ".";
    encoded += password;
 
  return encoded;
}

function ascii_to_hex(str)
{
	var arr1 = [];
	for (var n = 0, l = str.length; n < l; n ++) 
  {
		var hex = Number(str.charCodeAt(n)).toString(16);
		arr1.push(hex);
	}
	return arr1.join('');
}

function applyProfileUI(profileNum, tabButtonElement, queryCfg) {
  var psel = parseInt(profileNum, 10);
  if (!Number.isFinite(psel) || psel < 1 || psel > deviceProfileCount) {
    return;
  }
  var i, tabcontent, tablinks;
  tabcontent = document.getElementsByClassName("tabcontent");
  for (i = 0; i < tabcontent.length; i++) {
    tabcontent[i].style.display = "none";
  }
  tablinks = document.querySelectorAll('.tab .tablinks');
  for (i = 0; i < tablinks.length; i++) {
    tablinks[i].className = tablinks[i].className.replace(" active", "");
  }
  document.getElementById(profileNum).style.display = "block";
  if (tabButtonElement) {
    tabButtonElement.className += " active";
  }
  profile = profileNum;
  if (queryCfg !== false) {
    sendQuereCfg();
  }
}

function setProfile(evt, profileNum) {
  if (!portOpen){
    alert('Please connect the device first.');
    return;
  }
  if (parseInt(profileNum, 10) > deviceProfileCount) {
    alert('This profile is not available on the connected device.');
    return;
  }
  applyProfileUI(profileNum, evt.currentTarget);
}
