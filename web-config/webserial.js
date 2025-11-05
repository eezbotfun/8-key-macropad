/* 变量 */
let portOpen = false; // tracks whether a port is corrently open
let portPromise; // promise used to wait until port succesfully closed
let holdPort = null; // use this to park a SerialPort object when we change settings so that we don't need to ask the user to select it again

let port; // current SerialPort object
let reader; // current port reader object so we can call .cancel() on it to interrupt port reading
let profile = '0';
let version_number = 0;

/* 连接设备 */
document.getElementById('connectButton').addEventListener('click', () => {
  if (navigator.serial) {
    connectSerial();
  } else {
    alert('Web Serial API not supported.');
  }
});

/* 断开设备 */
document.getElementById('disconnectButton').addEventListener('click', () => {
    portOpen = false;
    //.releaseLock();
    //reader.cancel();
    document.getElementById('connectButton').hidden = false;
    document.getElementById('disconnectButton').hidden = true;
  
});

async function connectSerial() {
  const log = document.getElementById('device_rsp');
  log.textContent = "connecting ... " ;
  try {
    const filter = { usbVendorId : 0x303a ,usbProductId : 18 };
    port = await navigator.serial.requestPort({ filters: [filter] });
    
    await port.open({ baudRate: 115200 });
    
    const decoder = new TextDecoderStream();
    
    port.readable.pipeTo(decoder.writable);

    const inputStream = decoder.readable;
    reader = inputStream.getReader();

    // conceal connect button, display disconnect button instead
    document.getElementById('connectButton').hidden = true;
    document.getElementById('disconnectButton').hidden = false;

    portOpen = true;
    log.textContent = "Connected" ;

    sendQuereVersion();
    let rsp ="";

    while (portOpen && port.readable) {
      const { value, done } = await reader.read();
      if (value) {
        rsp += value;
        console.log('rsp : ', rsp);
        if ( rsp.length > 8 && rsp.includes("APP-VER=") ){
           // get version of APP
           let version = rsp.substring(8);
           version_number = parseInt( version, 10 );
           console.log('version is ', version_number);
           rsp = "";
        }
      }
      if (done) {
        console.log('[readLoop] DONE', done);
        reader.releaseLock();
        break;
      }
    }
    
    log.textContent = "disconnecting ... " ;
    await port.close();
    log.textContent = "disconnected" ;

  } catch (error) {
    log.innerHTML = error;
  }
}

/* 保存配置 */
document.getElementById('saveConfigButton').addEventListener('click', () => {
  if (navigator.serial) {
    if (portOpen){
      document.getElementById('saveConfigButton').hidden = true;
      sendConfig();
      document.getElementById('saveConfigButton').hidden = false;
    }
    else{
      alert('Please connect the device first.');
    }
    
  } else {
    alert('Web Serial API not supported.');
  }
});

/* input_str的第4个字节需要预留，用于填写负荷长度 */
function toU8Array(input_str){
  let len = input_str.length;
  let u8Array = new Uint8Array(len);

  for (let i = 0; i < len; i++) {
    u8Array[i] = input_str.charCodeAt(i);
  }
  
  // 填写协议里 负荷长度（不包含 magic_str 以及长度本身这个字节）
  u8Array[3] = len - 4; //
  console.log("u8Array : ",u8Array);
  return u8Array;
}
/* 发送按键定义配置到设备端 */
// Send a string over the serial port.
// This is easier than listening because we know when we're done sending
async function sendConfig() {
  let input = document.querySelector(".input").value// get the string to send from the term_input textarea

  if (!is_valid(input)){
      return;
  }

  //console.log("input : ",input);
  let encoded = encode(input);
  console.log("sending encoded : ",encoded);
  // Get a text encoder, pipe it to the SerialPort object, and get a writer
  //const textEncoder = new TextEncoderStream();
  //const writableStreamClosed = textEncoder.readable.pipeTo(port.writable);
  //const writer = textEncoder.writable.getWriter();
  writer = port.writable.getWriter();

  // write the encoded to the writer  
  await writer.write(toU8Array(encoded));

  let enable =  !document.getElementById('aliasconainer').hidden;
  let alias = document.querySelector(".alias_input").value;
  let encodedAliasConfig = encodeAliasConfig(enable, alias);
  await writer.write(toU8Array(encodedAliasConfig));

  enable =  !document.getElementById('scriptconainer').hidden;
  let script = document.querySelector(".script_input").value;
  let encodedScriptConfig = encodeScriptConfig(enable, script);
  await writer.write(toU8Array(encodedScriptConfig));
  
  // close the writer since we're done sending for now
  writer.releaseLock();

  //writer.close();
  //await writableStreamClosed;
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
  console.log("encoded : ",encoded);
  let u8Array = toU8Array(encoded)


  writer = port.writable.getWriter();
  await writer.write(u8Array);
  writer.releaseLock();
}

/* 发送获取设备版本号 */
async function sendQuereVersion() {
  
  /* 首字节插入 "ebf"type , 共4个字节 */
  /* 形如：[MAGIC_STR][TYPE][KEY_NUM]:CONFIG 
  如：ebf01.1:复制 (MAGIC_STR=ebf, KEY_NUM=1)*/
  let encoded = "ebf";  
  encoded += '0';//PAYLOAD_LEN
  encoded += '5';//type = 5 wifi配置
 
  console.log("encoded : ",encoded);
  let u8Array = toU8Array(encoded)


  writer = port.writable.getWriter();
  await writer.write(u8Array);
  writer.releaseLock();
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
  console.log('color = '+colorValue);
  let encoded = encodeLedConfig(colorValue, brightnessValue);
  console.log("sendLedConfig : ",encoded);

  let u8Array = toU8Array(encoded)


  writer = port.writable.getWriter();
  await writer.write(u8Array);
  writer.releaseLock();
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

/* 转换成设备可识别配置字符串 */
function encode(input){
  let modifierNum = getModifierNum(input);
  console.log("encoding ... modifierNum = ",modifierNum);
  let replaced = replaceModifier(input);

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
  encoded += (modifierNum>0?'1':'0');
  encoded += replaced; 

  console.log("encoded = ",encoded);

  let len = encoded.length - 4;

  console.log(" len = ",len);
  let len_in_hex = len.toString(16);
  console.log(" len_in_hex = ",len_in_hex);
 
  return encoded;
}
/* 转换成设备可识别配置字符串 */
function encodeAliasConfig(enable,input){

  console.log("encodeAliasConfig : input = "+ input + " len = " + input.length);

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
    encoded += input;
  }
  else{
    encoded += '0';//PAYLOAD_LEN
    encoded += '3';//type = 3 删除按键别名配置
    encoded += profile;
    encoded += ".";
    encoded += top.keyNumber;
    encoded += ":";
  }
  

  return encoded;
}

function encodeScriptConfig(enable,input){

  console.log("encodeScriptConfig : input = "+ input + " len = " + input.length);

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

function setProfile(evt, profileNum) {
  // Declare all variables
  var i, tabcontent, tablinks;

  // Get all elements with class="tabcontent" and hide them
  tabcontent = document.getElementsByClassName("tabcontent");
  for (i = 0; i < tabcontent.length; i++) {
    tabcontent[i].style.display = "none";
  }

  // Get all elements with class="tablinks" and remove the class "active"
  tablinks = document.getElementsByClassName("tablinks");
  for (i = 0; i < tablinks.length; i++) {
    tablinks[i].className = tablinks[i].className.replace(" active", "");
  }

  // Show the current tab, and add an "active" class to the button that opened the tab
  document.getElementById(profileNum).style.display = "block";
  evt.currentTarget.className += " active";

  profile = profileNum;
  console.log("current profile = "+profile);
  
}
