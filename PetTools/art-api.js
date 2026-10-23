// render-art 用：拼在 config / art / constants / logic / views 后面、同一个函数作用域里执行。
// 给网页版的 svgAvatar 一个假的 h()，让它吐出元素树，再序列化成一份独立的 SVG 文本。
//
// 网页版有几处长相是 CSS 决定的，这里照着 styles.js 处理：
//   .dshpet-whale-happy{display:none}                     平时不画笑眼「^ ^」
//   .dshpet-patted .dshpet-whale-happy{display:inline}    摸头时画笑眼……
//   .dshpet-patted .dshpet-whale-svg .dshpet-whale-eyes{display:none}   ……并藏起圆眼
// 动画（浮沉、眨眼、张嘴）不在静态图里，由 Unity 侧的程序动画补上。

// React 的桩在 ArtRenderer 里拼在源码最前面（views.js 顶层要读 React.memo）。

function h(type, props) {
  var children = [];
  for (var i = 2; i < arguments.length; i += 1) children.push(arguments[i]);
  return { type: type, props: props || {}, children: children };
}

// React 的驼峰属性名 → SVG 属性名。viewBox 这类本来就是驼峰的不动。
var KEEP_CAMEL = { viewBox: true, gradientTransform: true, gradientUnits: true, preserveAspectRatio: true };

function attrName(name) {
  if (name === "className") return "class";
  if (KEEP_CAMEL[name]) return name;
  return name.replace(/[A-Z]/g, function (c) { return "-" + c.toLowerCase(); });
}

function escapeXml(text) {
  return String(text).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

function hasClass(node, cls) {
  var c = node.props && node.props.className;
  return typeof c === "string" && (" " + c + " ").indexOf(" " + cls + " ") >= 0;
}

function serialize(node, patted) {
  if (node === null || node === undefined || node === false) return "";
  if (Array.isArray(node)) return node.map(function (n) { return serialize(n, patted); }).join("");
  if (typeof node !== "object") return escapeXml(node);
  if (hasClass(node, "dshpet-whale-happy") && !patted) return "";
  if (hasClass(node, "dshpet-whale-eyes") && patted) return "";
  var attrs = "";
  var props = node.props;
  for (var key in props) {
    if (!Object.prototype.hasOwnProperty.call(props, key)) continue;
    if (key === "key" || key === "style" || key === "role" || key === "children") continue;
    if (key.indexOf("aria-") === 0 || key.indexOf("data-") === 0) continue;
    var value = props[key];
    if (value === null || value === undefined || value === false) continue;
    attrs += " " + attrName(key) + "=\"" + escapeXml(value) + "\"";
  }
  if (node.type === "svg") attrs += " xmlns=\"http://www.w3.org/2000/svg\"";
  var inner = serialize(node.children, patted);
  return "<" + node.type + attrs + (inner === "" ? "/>" : ">" + inner + "</" + node.type + ">");
}

/**
 * 一张图：look 是 { level, form }，state 是 normal / hungry / sleep / sad / excited / pat，
 * dim 是情绪三维的 key（只在 normal 下有意义）或 null。
 */
function __renderSvg(level, form, state, dim) {
  var dimEntry = null;
  if (dim) {
    for (var i = 0; i < MOOD_DIMS.length; i += 1) if (MOOD_DIMS[i].key === dim) dimEntry = MOOD_DIMS[i];
  }
  var tree = svgAvatar({
    tier: state === "excited" ? "epic" : "normal",
    hungry: state === "hungry",
    asleep: state === "sleep",
    sad: state === "sad",
    level: level,
    form: form,
    dim: dimEntry
  });
  return serialize(tree, state === "pat");
}

globalThis.__art = {
  render: __renderSvg,
  forms: function () { return JSON.stringify(FORMS.map(function (f) { return f.key; })); },
  dims: function () { return JSON.stringify(MOOD_DIMS.map(function (d) { return d.key; })); }
};
