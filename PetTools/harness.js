// PetTools 的 JS 侧：在 Jint 里把原 store 跑起来，给它一个假时钟、假定时器、种子随机数和假 localStorage。
// 原 JS（spec/ 下的 config / art / constants / logic / store，网页版插件的快照）由 JsHost 拼在这段后面执行。
//
// 约定（C# 的 PetCore 回放轨迹时照做一遍，两边才可比）：
//   - Date.now() 恒等于 __now；定时器不会自己跑，只在 __advance(now) 里按（到期时刻, 序号）依次触发，
//     触发时 __now 等于该定时器的到期时刻；
//   - Math.random 是 mulberry32(__seed)；
//   - 本地时区由 Jint 的 LocalTimeZone 决定（轨迹文件里记着偏移）。

var __now = 0;
Date.now = function () { return __now; };

var __timers = [];
var __timerSeq = 0;
function setTimeout(fn, ms) {
  __timerSeq += 1;
  __timers.push({ id: __timerSeq, at: __now + (ms > 0 ? ms : 0), fn: fn });
  return __timerSeq;
}
function clearTimeout(id) {
  for (var i = 0; i < __timers.length; i += 1) {
    if (__timers[i].id === id) { __timers.splice(i, 1); return; }
  }
}
function __advance(now) {
  for (;;) {
    var at = -1;
    for (var i = 0; i < __timers.length; i += 1) {
      var t = __timers[i];
      if (t.at > now) continue;
      if (at < 0 || t.at < __timers[at].at || (t.at === __timers[at].at && t.id < __timers[at].id)) at = i;
    }
    if (at < 0) break;
    var due = __timers.splice(at, 1)[0];
    __now = due.at;
    due.fn();
  }
  __now = now;
}

var __seed = 1;
Math.random = function () {
  // mulberry32
  __seed = (__seed + 0x6D2B79F5) | 0;
  var t = __seed;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};

var __storage = {};
var window = {
  localStorage: {
    getItem: function (k) { return Object.prototype.hasOwnProperty.call(__storage, k) ? __storage[k] : null; },
    setItem: function (k, v) { __storage[k] = String(v); },
    removeItem: function (k) { delete __storage[k]; }
  }
};
var console = { warn: function () {}, log: function () {} };
