// 拼在原 JS 源码后面、同一个函数作用域里执行：能直接摸到 DEFAULTS / createPetStore 这些内部名字。
// 导出到 globalThis.__pet，由 C# 调用。快照和操作的定义必须和 C# 侧（Core.Tests 的 TraceReplay）一一对应。

var __store = null;
var __config = null;

function __boot(overridesJson, savedState) {
  if (overridesJson !== null) window.localStorage.setItem(CONFIG_KEY, overridesJson);
  if (savedState !== null) window.localStorage.setItem(STATE_KEY, savedState);
  __config = resolveConfig();
  __store = createPetStore(__config);
}

/** 宠物此刻的快照：只放规则层的东西（界面开关、拖拽、头像画法不在里面），id 换成在 petOrder 里的位置。 */
function __snap() {
  var s = __store.getState();
  var col = __store.getCollection();
  var g = __store.getGlobalStats();
  var p = s.pet;
  var skills = {};
  SKILLS.forEach(function (item) {
    var row = s.skills[item.key];
    skills[item.key] = { xp: row.xp, level: row.level, mastery: row.mastery };
  });
  var m = s.memory;
  var d = s.daily;
  return {
    pet: {
      name: p.name, species: p.species, avatar: p.avatar, icon: p.icon,
      mood: p.mood, hunger: p.hunger, energy: p.energy,
      curiosity: p.curiosity, pride: p.pride, concern: p.concern,
      exp: p.exp, level: p.level, form: p.form
    },
    comboCount: s.comboCount,
    comboMultiplier: s.comboMultiplier,
    comboTier: s.comboTier,
    eatKey: s.eatKey,
    patKey: s.patKey,
    lastAct: s.lastAct,
    idleAct: s.idleAct,
    totalFeeds: s.totalFeeds,
    totalTokens: s.totalTokens,
    tokensBySource: {
      user_input: s.tokensBySource.user_input,
      generation: s.tokensBySource.generation,
      tool_result: s.tokensBySource.tool_result
    },
    snacks: s.snacks,
    achievements: s.achievements.slice(),
    daily: {
      day: d.day, feeds: d.feeds, tools: d.tools, bestCombo: d.bestCombo,
      pats: d.pats, tokens: d.tokens, snacks: d.snacks, done: d.done.slice()
    },
    streakDay: s.streakDay,
    streakCount: s.streakCount,
    pats: s.pats,
    skills: skills,
    memory: {
      files: m.files.map(function (r) { return { name: r.name, count: r.count }; }),
      tools: m.tools.map(function (r) { return { name: r.name, count: r.count }; }),
      hours: m.hours.slice(),
      bornDay: m.bornDay, errors: m.errors, recoveries: m.recoveries
    },
    lastFeedAt: s.lastFeedAt,
    asleep: s.asleep,
    bubble: s.bubble === null ? null : { key: s.bubble.key, kind: s.bubble.kind, text: s.bubble.text },
    buff: s.buff === null ? null : { kind: s.buff.kind, until: s.buff.until },
    effects: s.effects.map(function (e) {
      return {
        key: e.key, icon: e.icon, text: e.text === undefined ? null : e.text,
        foodAmount: e.foodAmount, expAmount: e.expAmount, tokens: e.tokens, foodTier: e.foodTier,
        source: e.source, comboCount: e.comboCount, comboMultiplier: e.comboMultiplier, tier: e.tier
      };
    }),
    miniGame: s.miniGame === null ? null : {
      targets: s.miniGame.targets.map(function (t) { return { id: t.id, icon: t.icon, x: t.x, y: t.y, caught: t.caught }; }),
      current: s.miniGame.current, startedAt: s.miniGame.startedAt, deadline: s.miniGame.deadline,
      score: s.miniGame.score, finished: s.miniGame.finished, success: s.miniGame.success
    },
    hatching: s.hatchingEgg === null ? null : s.hatchingEgg.type,
    eggs: s.eggs.map(function (e) { return { type: e.type, obtainedAt: e.obtainedAt }; }),
    talent: s.talent,
    prestige: s.prestige,
    formsOwned: s.formsOwned.slice(),
    petCount: s.petCount,
    activeIndex: s.petOrder.indexOf(s.activePetId),
    favorites: s.favorites.map(function (id) { return s.petOrder.indexOf(id); }),
    pets: s.petOrder.map(function (id) {
      var r = col[id];
      return { species: r.species, name: r.name, level: r.pet.level, prestige: r.prestige || 0, bornAt: r.bornAt };
    }),
    global: {
      totalTokensAllTime: g.totalTokensAllTime,
      totalFeedsAllTime: g.totalFeedsAllTime,
      achievementsUnlockedAllTime: g.achievementsUnlockedAllTime,
      petsHatched: g.petsHatched,
      eggsObtained: g.eggsObtained.slice()
    }
  };
}

/**
 * 执行一步。event 的形状就是桥的统一事件（只用到 kind / tokens / output / tool / callId / file / failed），
 * 路由照抄 plugin.js：tool_call 只观察；tool_result 先看成败再喂；user_input / generation 直接喂。
 */
function __op(op, a, b) {
  var now = Date.now();
  var s = __store;
  switch (op) {
    case "event":
      if (a.kind === "tool_call") {
        if (typeof a.tool === "string" && a.tool !== "") {
          s.observeTool({ callId: a.callId || "", name: a.tool, file: a.file || null }, now);
        }
        return null;
      }
      if (a.kind === "tool_result") {
        s.observeToolResult({ callId: a.callId || "", failed: a.failed === true }, now);
        s.feed("tool_result", a.tokens, now, 0);
        return null;
      }
      if (a.kind === "user_input") { s.feed("user_input", a.tokens, now, 0); return null; }
      if (a.kind === "generation") { s.feed("generation", a.tokens, now, a.output); return null; }
      return null;
    case "advance": return null;
    case "tick": s.tick(); return null;
    case "snack": return s.snack();
    case "pat": return s.pat();
    case "prestige": return s.prestige();
    case "switchForm": return s.switchForm(a);
    case "switchPet": s.switchPet(a); return null;
    case "toggleFavorite": s.toggleFavorite(); return null;
    case "addPet": return s.addPet(a, b);
    case "renamePet": return s.renamePet(a);
    case "hatchFirst": {
      var eggs = s.getEggs();
      if (eggs.length === 0) return false;
      s.startHatch(eggs[0].id);
      return s.confirmHatch(a);
    }
    case "minigame.start": return s.startMiniGame();
    case "minigame.hit": {
      var game = s.getState().miniGame;
      s.hitMiniGameTarget(game === null ? 0 : game.current);
      return null;
    }
    case "minigame.fail": s.failMiniGame(); return null;
    case "gm.setLevel": s.gm.setLevel(a); return null;
    case "gm.addExp": s.gm.addExp(a); return null;
    case "gm.setHunger": s.gm.setHunger(a); return null;
    case "gm.setMood": s.gm.setMood(a); return null;
    case "gm.setEnergy": s.gm.setEnergy(a); return null;
    case "gm.fillSnacks": s.gm.fillSnacks(); return null;
    case "gm.grantEggs": s.gm.grantEggs(); return null;
    case "gm.simulateFeed": s.gm.simulateFeed(a); return null;
    case "reboot":
      // 等同网页刷新：落盘，然后所有定时器作废。
      s.dispose();
      __timers.length = 0;
      // 带参数 = 关掉之后过了这么久才重新打开。
      if (typeof a === "number") __now += a;
      __store = createPetStore(__config);
      return null;
    default:
      throw new Error("未知操作 " + op);
  }
}

/** 数据表：PetTools export-data 写成 pet-data.json。带函数的条目只导出它的数据部分，判定逻辑在 C# 里。 */
function __data() {
  function keys(o) { return Object.keys(o); }
  function countOf(table) {
    var out = {};
    keys(table).forEach(function (k) { out[k] = table[k].length; });
    return out;
  }
  return {
    defaults: DEFAULTS,
    constants: {
      stateVersion: STATE_VERSION, maxPets: MAX_PETS, maxEggs: MAX_EGGS, expPerLevel: EXP_PER_LEVEL,
      memoryToolTop: MEMORY_TOOL_TOP, memorySlack: MEMORY_SLACK, callNameCap: CALL_NAME_CAP,
      dailyQuestCount: DAILY_QUEST_COUNT, skillFallback: SKILL_FALLBACK
    },
    icons: { snack: SNACK_ICON, evolve: EVOLVE_ICON, pat: PAT_ICON, quest: QUEST_ICON, badge: BADGE_ICON },
    baseExp: BASE_EXP,
    foodIcon: FOOD_ICON,
    bubbleLines: BUBBLE_LINES,
    skills: SKILLS,
    toolSkill: TOOL_SKILL,
    editTools: keys(EDIT_TOOLS),
    whaleStages: WHALE_STAGES,
    forms: FORMS.map(function (f) { return { key: f.key, skill: f.skill, label: f.label, icon: f.icon, size: f.size, eyeGrow: f.eyeGrow }; }),
    moodDims: MOOD_DIMS,
    dimLineKinds: keys(DIM_LINE_KINDS),
    careKinds: keys(CARE_KINDS),
    idleActs: IDLE_ACTS,
    achievements: ACHIEVEMENTS.map(function (a) { return { id: a.id, icon: a.icon, label: a.label, hint: a.hint }; }),
    questPool: DAILY_QUEST_POOL.map(function (q) { return { id: q.id, icon: q.icon, label: q.label, goal: q.goal }; }),
    eggTypes: EGG_TYPES,
    petSpecies: PET_SPECIES,
    eggMilestones: EGG_MILESTONES.map(function (m) { return { id: m.id, egg: m.egg }; }),
    milestoneLevels: MILESTONE_LEVELS,
    titles: TITLES.map(function (t) { return t.label; }),
    // 下面这几张是函数表，C# 里手写；导出条数是为了让 C# 测试发现「JS 那边加了一条」。
    dynamicLineCounts: countOf(DYNAMIC_LINES),
    adviceLines: keys(ADVICE_LINES),
    chatLines: keys(CHAT_LINES)
  };
}

globalThis.__pet = {
  boot: __boot, snap: __snap, op: __op, data: __data,
  storage: function () { return window.localStorage.getItem(STATE_KEY); }
};
