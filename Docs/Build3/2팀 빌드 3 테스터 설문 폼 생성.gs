/**
 * 2팀 빌드 3 테스터 설문 — 구글 폼 생성 스크립트
 *
 * ── 사용법 ────────────────────────────────────────────────
 * 1. https://script.google.com 접속 → "새 프로젝트"
 * 2. 기본으로 열린 Code.gs 내용을 전부 지우고 이 파일 내용을 붙여넣기
 * 3. 상단 함수 선택란에서 createForm 선택 → "실행"
 * 4. 최초 실행 시 권한 승인 (내 계정 선택 → 고급 → 안전하지 않음으로 이동 → 허용)
 * 5. 실행 로그(Ctrl+Enter)에 찍힌 편집 URL / 응답 URL 확인
 *
 * ── 응답 취합 ─────────────────────────────────────────────
 * 폼 편집 화면 → "응답" 탭 → 스프레드시트 아이콘을 누르면
 * 응답이 구글 시트로 자동 누적된다.
 *
 * ── 문항 수정 ─────────────────────────────────────────────
 * 아래 CONFIG 상수만 고치면 된다. 문항을 추가/삭제하려면
 * 해당 섹션의 addChoice(...) / form.addParagraphTextItem() 호출을 편집.
 * ─────────────────────────────────────────────────────────
 */

var CONFIG = {
  title: '2팀 빌드 3 테스터 설문',
  description: [
    'NorthLand: Last Stand — Build 3 테스터 설문입니다.',
    '',
    '[플레이 안내]',
    '· zip 압축을 풀고 exe 실행 → 타이틀 화면에서 "게임 시작"',
    '· 카메라: WASD 또는 우클릭 드래그로 이동, 휠로 줌',
    '· 목표: 낮에 자원을 모으고 방어를 준비 → 밤에 몰려오는 몬스터로부터 본진을 지킨다',
    '· 가능하면 7웨이브까지 플레이해 주세요. 중간에 게임오버되면 다시 시작해도 됩니다.',
    '',
    '플레이 20분 + 설문 5~10분 정도 걸립니다. "이유" 칸은 비워도 됩니다.'
  ].join('\n'),
  // 각 객관식 문항 뒤에 이유 칸(장문)을 붙일지 여부
  includeReasonFields: true
};

// 반복적으로 쓰는 선택지 세트
var SCALE_INCONVENIENT = [
  '전혀 안 불편했다',
  '조금 불편했다',
  '꽤 불편했다',
  '매우 불편했다'
];

var SCALE_CONFUSING = [
  '전혀 안 헷갈렸다',
  '조금 헷갈렸다',
  '꽤 헷갈렸다',
  '매우 헷갈렸다'
];

var SCALE_LOSS = [
  '전혀 그렇지 않다',
  '조금 손해 같다',
  '꽤 손해 같다',
  '매우 손해 같다'
];

function createForm() {
  var form = FormApp.create(CONFIG.title);
  form.setTitle(CONFIG.title);
  form.setDescription(CONFIG.description);
  form.setProgressBar(true);
  form.setCollectEmail(false);

  // ── 기본 정보 ────────────────────────────────────────────
  section(form, '기본 정보');

  text(form, '이름 / 닉네임', true);
  text(form, '플레이 시간 (분)', true);
  text(form, '도달한 최고 웨이브', true);
  choice(form, '클리어 여부', [
    '7웨이브 클리어',
    '도중 게임오버',
    '도중에 그만둠'
  ], true, null, false);

  // ── 경영 공간 ────────────────────────────────────────────
  section(form, '경영 공간');

  choice(form,
    '1. 영토 확장이 하루에 1개씩만 됩니다. 매일 낮마다 하나씩 확보하는 반복이 귀찮았나요?',
    SCALE_INCONVENIENT, true);

  choice(form,
    '2. 본진을 올려야 다른 건물의 레벨 상한이 풀립니다. 이걸 알아채기 전에 "왜 업그레이드가 막혀 있지?" 하고 답답했나요?',
    SCALE_INCONVENIENT, true);

  choice(form,
    '3. 자원이 8종입니다 (나무 · 철 · 식량 · 마나석 + 금 · 루비 · 사파이어 · 다이아). 각 자원이 어디에 쓰이는지 파악됐나요?',
    ['잘 파악됐다', '대충 알았다', '헷갈렸다', '끝까지 모르겠다'],
    true, '특히 헷갈린 자원이 있으면 아래에 적어주세요.');

  choice(form,
    '4. 마나석을 쓸 곳이 본진 · 마법 연구소 · 연금술사의 집 3곳입니다. 어디에 먼저 써야 할지 헷갈렸나요?',
    SCALE_CONFUSING, true);

  choice(form,
    '5. 마나석 수급량이 쓰고 싶은 만큼 충분했나요?',
    ['부족했다', '적당했다', '남았다'], true);

  choice(form,
    '6. 연금술사의 집에서 마나석을 다른 자원으로 바꿀 수 있습니다. 앞으로 계속 플레이한다면 이 교환을 자주 쓸 것 같나요?',
    ['자주 쓸 것 같다', '필요할 때만 쓸 것 같다', '거의 안 쓸 것 같다', '이 기능을 몰랐다'],
    true);

  choice(form,
    '7. 낮을 끝낼 때 확인 팝업이 뜹니다. 도움이 됐나요, 매번 뜨는 게 귀찮았나요?',
    ['도움이 됐다', '상관없다', '귀찮았다'], true);

  // ── 전투 공간 ────────────────────────────────────────────
  section(form, '전투 공간');

  choice(form,
    '8. 타워 배치가 낮에만 가능합니다. 밤에 몬스터를 보고 추가로 배치하지 못하는 게 불편했나요?',
    SCALE_INCONVENIENT, true);

  choice(form,
    '9. 타워 합성도 낮에만 가능합니다. 불편했나요?',
    SCALE_INCONVENIENT, true);

  choice(form,
    '10. 합성하면 재료로 쓴 타워가 사라집니다. 손해처럼 느껴졌나요?',
    SCALE_LOSS, true);

  choice(form,
    '11. 지금은 어떤 타워를 조합하면 어떤 타워가 나오는지 보여주는 "족보 패널"이 없습니다. 만약 족보 패널이 있다면, 매번 족보를 확인하고 나서 타워를 고를 것 같나요?',
    ['매번 확인할 것 같다', '처음 몇 번만 보고 외울 것 같다', '거의 안 볼 것 같다', '잘 모르겠다'],
    true);

  // ── 전체 흐름 ────────────────────────────────────────────
  section(form, '전체 흐름');

  choice(form,
    '12. 경영 공간과 전투 공간을 오가는 것이 번거로웠나요?',
    SCALE_INCONVENIENT, true);

  choice(form,
    '13. 밤 페이즈가 늘어져서 배속을 켰나요?',
    ['배속 없이도 괜찮았다', '켜니까 편했다', '배속 없으면 못 버틴다', '배속 기능을 몰랐다'],
    true);

  choice(form,
    '14. 한 판(7웨이브) 길이가 어땠나요?',
    ['너무 짧다', '적절하다', '늘어진다'], true);

  // 15번은 복수 선택 — 이유 칸은 별도 문구로
  var bossItem = form.addCheckboxItem();
  bossItem.setTitle('15. 최종 보스가 여러 행동을 합니다. 이 중 알아챈 게 있나요? (복수 선택)');
  bossItem.setChoiceValues([
    '본진 쪽으로 갑자기 빠르게 돌진했다',
    '느려지는 대신 단단해졌다',
    '주변 타워의 공격 속도가 떨어졌다',
    '계속 잡몹을 불러냈다',
    '아무것도 못 알아챘다',
    '보스를 못 만났다'
  ]);
  bossItem.setRequired(true);

  if (CONFIG.includeReasonFields) {
    form.addParagraphTextItem()
      .setTitle('그 외 눈에 띈 보스 행동이 있으면 적어주세요')
      .setRequired(false);
  }

  // ── 자유 응답 · 버그 제보 ────────────────────────────────
  section(form, '자유 응답 · 버그 제보');

  form.addParagraphTextItem()
    .setTitle('16. 가장 답답했던 순간')
    .setRequired(false);

  form.addParagraphTextItem()
    .setTitle('17. 가장 재밌었던 순간 / 그 외 하고 싶은 말')
    .setRequired(false);

  form.addParagraphTextItem()
    .setTitle('버그 제보')
    .setHelpText([
      '발견한 버그가 있으면 아래 형식으로 적어주세요. 여러 개면 줄을 나눠서 적어주시면 됩니다.',
      '',
      '· 현상 / 재현 방법 / 발생 시점(웨이브 · 낮or밤) / 심각도(치명·보통·사소)',
      '',
      '예) 보상 창이 떠 있는데 미니맵이 눌린다 / 웨이브 클리어 후 보상 창에서 미니맵 클릭 / 3웨이브 종료 직후 / 보통'
    ].join('\n'))
    .setRequired(false);

  // ── 결과 출력 ────────────────────────────────────────────
  Logger.log('폼이 생성되었습니다.');
  Logger.log('편집 URL : ' + form.getEditUrl());
  Logger.log('응답 URL : ' + form.getPublishedUrl());
  Logger.log('');
  Logger.log('응답을 스프레드시트로 받으려면: 편집 화면 → 응답 탭 → 스프레드시트 아이콘 클릭');

  return form.getEditUrl();
}

// ── 헬퍼 ──────────────────────────────────────────────────

/** 섹션(페이지) 구분을 추가한다. */
function section(form, title) {
  form.addPageBreakItem().setTitle(title);
}

/** 단답 문항을 추가한다. */
function text(form, title, required) {
  form.addTextItem().setTitle(title).setRequired(!!required);
}

/**
 * 객관식 문항을 추가한다.
 * @param {Form} form 대상 폼
 * @param {string} title 질문
 * @param {string[]} choices 선택지
 * @param {boolean} required 필수 여부
 * @param {string=} reasonHelpText 이유 칸에 붙일 안내문 (없으면 기본 문구)
 * @param {boolean=} withReason 이유 칸을 붙일지 (기본 true)
 */
function choice(form, title, choices, required, reasonHelpText, withReason) {
  var item = form.addMultipleChoiceItem();
  item.setTitle(title);
  item.setChoiceValues(choices);
  item.setRequired(!!required);

  var attachReason = (withReason === undefined) ? true : withReason;
  if (CONFIG.includeReasonFields && attachReason) {
    var reason = form.addParagraphTextItem();
    reason.setTitle('↳ 이유 (선택)');
    if (reasonHelpText) {
      reason.setHelpText(reasonHelpText);
    }
    reason.setRequired(false);
  }
}
