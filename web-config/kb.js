//import Keyboard from './node_modules/simple-keyboard';
//import './node_modules/simple-keyboard/build/css/index.css';
let Keyboard = window.SimpleKeyboard.default;

/* ------  eezbotfun键盘 ------ */
let micropad = new Keyboard(".keyboard1",{
  onKeyPress: button => onMicropadPress(button),
  layout: {
      'default': [
        '1 2 3 4',
        '5 6 7 8'
      ]
    }
});

let lastKeyNumber='0';
/* 用户选择了快捷键盘的某个按键 */
function onMicropadPress(button) {
    
  console.log("key selected ", button);
  
  /* 高亮当前按键 */
  micropad.getButtonElement(button).style.background = "#9ab4d0";
  micropad.getButtonElement(button).style.color = "blue";

  /* 恢复之前高亮按键 */
  if (lastKeyNumber && lastKeyNumber != button ) {
    top.keyNumber = button;
    if(micropad.getButtonElement(lastKeyNumber))
        micropad.getButtonElement(lastKeyNumber).removeAttribute("style");
    sendQuereCfg();
  }

  lastKeyNumber = button;
  document.getElementById('saveConfigButton').hidden = false;
}
  
/*  ------  配置录入键盘  ------  */
let commonKeyboardOptions = {
  onChange: input => onChange(input),
  onKeyPress: button => onKeyPress(button),
  theme: "simple-keyboard hg-theme-default hg-layout-default",
  physicalKeyboardHighlight: true,
  syncInstanceInputs: true,
  mergeDisplay: true,
  tabCharOnTab: true,
  //debug: true
};
let keyboard = new Keyboard(".simple-keyboard-main",{
  onChange: input => onChange(input),
  onKeyPress: button => onKeyPress(button),
  theme: "simple-keyboard hg-theme-default hg-layout-default",
  physicalKeyboardHighlight: true,
  syncInstanceInputs: true,
  mergeDisplay: true,
  tabCharOnTab: true,
  layout: {
    'default': [
      "{escape} {f1} {f2} {f3} {f4} {f5} {f6} {f7} {f8} {f9} {f10} {f11} {f12}",
      '` 1 2 3 4 5 6 7 8 9 0 - = {bkspace}',
      '{tab} q w e r t y u i o p [ ] \\',
      '{lock} a s d f g h j k l ; \' {enter}',
      '{lshift} z x c v b n m , . / {rshift}',
      '{lctrl} {lgui} {lalt} {space} {ralt} {rgui} {rctrl}'

    ],
    'shift': [
      "{escape} {f1} {f2} {f3} {f4} {f5} {f6} {f7} {f8} {f9} {f10} {f11} {f12}",
      '~ ! @ # $ % ^ &amp; * ( ) _ + {bkspace}',
      '{tab} Q W E R T Y U I O P { } |',
      '{lock} A S D F G H J K L : " {enter}',
      '{lshift} Z X C V B N M &lt; &gt; ? {rshift}',
      '{lctrl} {lgui} {lalt} {space} {ralt} {rgui} {rctrl}'
    ]
  },
  display:{
    "{lctrl}":"ctrl",
    "{lalt}":"alt",
    "{lshift}":"shift",
    "{rctrl}":"ctrl",
    "{ralt}":"alt",
    "{rshift}":"shift",
    "{bkspace}":"backspace",
    "{lgui}":"win/cmd⌘",
    "{rgui}":"win/cmd⌘"
  }
});

let keyboardControlPad = new Keyboard(".simple-keyboard-control", {
  ...commonKeyboardOptions,
  layout: {
    default: [
      "{prtscr} {scrolllock} {pause}",
      "{insert} {home} {pageup}",
      "{delete} {end} {pagedown}"
    ]
  }
});

let keyboardArrows = new Keyboard(".simple-keyboard-arrows", {
  ...commonKeyboardOptions,
  layout: {
    default: ["{arrowup}", "{arrowleft} {arrowdown} {arrowright}"]
  }
});

/* 用户直接在输入栏，通过物理键盘录入的情况，同步更新keyboard的input值 */
document.querySelector(".input").addEventListener("input", event => {
  //console.log("input = ",event.target.value);
  keyboard.setInput(event.target.value);
});

//console.log(keyboard);
/* simple-keyboard 会自动加字符到input（除非是控制字符，即{}内字符） */
let ctrlButtonNum = 0;
function onChange(input) {
    //console.log("2 Input changed ", input);

    /* 需要以下赋值，否则显示栏不会同步 */
    document.querySelector(".input").value = input;
 
}
/* 先回调onKeyPress ，用于控制字符（即{}内字符）处理
   然后才会调用onChange */
function onKeyPress(button) {
  
    //console.log("1 Button pressed", button);
    //console.log("1 keyboar.input = ", keyboard.getInput());
    let input = keyboard.getInput();

    if (button === "{lock}"){
        /* 键盘大小写切换 */
        handleShift();
    } 
    /* 用户录入控制字符 */
    /* 控制字符和普通字符一起按下（发送给PC）的情况： 
    我们通过特殊ascii码 标识需要同时按下的按键组合（如 CTRL + C ） */
    else {
        modifiedInput = input + button;
        
        //console.log("modifiedInput = ",modifiedInput);
        document.querySelector(".input").value = modifiedInput;
        keyboard.setInput(modifiedInput);
            
    }
}

function handleShift() {
    let currentLayout = keyboard.options.layoutName;
    let shiftToggle = currentLayout === "default" ? "shift" : "default";

    keyboard.setOptions({
        layoutName: shiftToggle
    });
}

/* 回删配置字符 */
document.getElementById('deleteButton').addEventListener('click', () => {
  let input = keyboard.getInput();
  
  if ( input.charAt(input.length-1) === '}' ){
      let pos = input.lastIndexOf('{');
      modifiedInput = input.slice(0,pos);
  }/*
  else if ( input.charAt(input.length-1) === "\x03" ){
      let pos = input.lastIndexOf("\x02");
      modifiedInput = input.slice(0,pos);
  }*/
  else{
      modifiedInput = input.slice(0,-1);
  }
  
  document.querySelector(".input").value = modifiedInput;
  keyboard.setInput(modifiedInput);
});








