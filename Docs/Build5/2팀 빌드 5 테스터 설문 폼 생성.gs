/**
 * 2팀 빌드 5 테스터 설문 — 구글 폼 생성 스크립트
 *
 * ── 사용법 ────────────────────────────────────────────────
 * 1. https://script.google.com 접속 → "새 프로젝트"
 * 2. 기본으로 열린 Code.gs 내용을 전부 지우고 이 파일 내용을 붙여넣기
 * 3. 상단 함수 선택란에서 createForm 선택 → "실행"
 * 4. 최초 실행 시 권한 승인 (내 계정 선택 → 고급 → 안전하지 않음으로 이동 → 허용)
 * 5. 실행 로그(Ctrl+Enter)에 찍힌 편집 URL / 응답 URL 확인
 *    → 응답 URL을 `2팀 빌드 5 체크리스트.txt`에 저장한다 (빌드 3과 동일)
 *
 * ── 응답 취합 ─────────────────────────────────────────────
 * 폼 편집 화면 → "응답" 탭 → 스프레드시트 아이콘을 누르면
 * 응답이 구글 시트로 자동 누적된다.
 *
 * ── 문항 구성 ─────────────────────────────────────────────
 * 문항 5개 + 각 문항마다 자유 의견(장문) 칸.
 * 문항을 추가하려면 choice(...) / text(...) 호출을 추가하면 된다.
 * ─────────────────────────────────────────────────────────
 */

var CONFIG = {
  title: '2팀 빌드 5 테스터 설문',
  description: [
    'NorthLand: Last Stand — Build 5 테스터 설문입니다.',
    '',
    '[플레이 안내]',
    '· zip 압축을 풀고 exe 실행 → 타이틀에서 세이브 슬롯을 하나 고른 뒤 "게임 시작"',
    '· 카메라: WASD 또는 우클릭 드래그로 이동, 휠로 줌',
    '· 목표: 낮에 자원을 모으고 방어를 준비 → 밤에 몰려오는 몬스터로부터 본진을 지킨다',
    '· 한 판은 15웨이브입니다. 끝까지 못 가도 괜찮습니다.',
    '',
    '설문은 5분 정도 걸립니다. "자유 의견" 칸은 비워도 됩니다.'
  ].join('\n'),
  // 각 문항 뒤에 자유 의견 칸(장문)을 붙일지 여부
  includeOpinionFields: true
};

function createForm() {
  var form = FormApp.create(CONFIG.title);
  form.setTitle(CONFIG.title);
  form.setDescription(CONFIG.description);
  form.setProgressBar(true);
  form.setCollectEmail(false);

  text(form, '1. 최대 몇 웨이브까지 갔나요? (숫자로 적어주세요 · 한 판은 15웨이브입니다)', true);
  opinion(form, '몇 번째 웨이브에서, 무엇 때문에 멈췄는지 자유롭게 적어주세요.');

  choice(form,
    '2. 매 판을 할 때마다 다른 조합(빌드)을 세워서 공략을 찾아가는 느낌이 있었나요?',
    [
      '판마다 다른 조합을 시도하게 됐다',
      '조금 달라지긴 했다',
      '결국 매번 같은 방식으로 하게 됐다',
      '한 판만 해봐서 모르겠다'
    ],
    true,
    '어떤 조합을 시도했는지, 왜 그 방식으로 굳어졌는지 자유롭게 적어주세요.');

  choice(form,
    '3. 경영 공간의 건물에 지금은 이펙트(파티클 · 강조 표시)만 들어가 있습니다. 어떤 것이 상호작용 가능한 건물인지 잘 보였나요? 건물 위에 아이콘 이미지를 넣으면 좋을까요?',
    [
      '잘 보였다 — 아이콘은 없어도 된다',
      '잘 보였지만 아이콘이 있으면 더 좋겠다',
      '잘 안 보였다 — 아이콘이 필요하다',
      '잘 안 보였지만 아이콘 말고 다른 방법이 좋겠다'
    ],
    true,
    '어느 건물이 특히 안 보였는지, 아이콘이라면 어떤 형태(항상 표시 · 마우스를 올렸을 때만 · 줌 아웃할 때만 등)가 좋을지 자유롭게 적어주세요.');

  choice(form,
    '4. 타워 합성(설치된 타워 여러 개를 재료로 더 강한 타워를 만드는 것)에 장점이 있다고 느꼈나요?',
    [
      '확실히 이득이라 자주 썼다',
      '이득인 것 같아서 가끔 썼다',
      '재료가 아까워서 잘 안 썼다',
      '이득인지 아닌지 모르겠다',
      '합성을 안 해봤다 / 몰랐다'
    ],
    true,
    '어떤 점이 좋았는지 / 왜 손해처럼 느껴졌는지 자유롭게 적어주세요.');

  choice(form,
    '5. 그날 타워를 하나도 배치하지 않고 밤으로 넘어가려 하면 확인 팝업이 뜹니다. 필요한 기능이라고 생각하나요?',
    [
      '필요하다 — 실수를 막아줬다',
      '있어도 그만 없어도 그만이다',
      '불필요하다 — 매번 뜨는 게 귀찮았다',
      '팝업을 못 봤다'
    ],
    true,
    '팝업이 뜬 상황과 그때 느낌을 자유롭게 적어주세요.');

  // ── 결과 출력 ────────────────────────────────────────────
  Logger.log('폼이 생성되었습니다.');
  Logger.log('편집 URL : ' + form.getEditUrl());
  Logger.log('응답 URL : ' + form.getPublishedUrl());
  Logger.log('');
  Logger.log('응답 URL을 "2팀 빌드 5 체크리스트.txt"에 저장하세요.');
  Logger.log('응답을 스프레드시트로 받으려면: 편집 화면 → 응답 탭 → 스프레드시트 아이콘 클릭');

  return form.getEditUrl();
}

// ── 헬퍼 ──────────────────────────────────────────────────

/** 단답 문항을 추가한다. */
function text(form, title, required) {
  form.addTextItem().setTitle(title).setRequired(!!required);
}

/** 자유 의견(장문) 칸을 단독으로 추가한다. */
function opinion(form, helpText) {
  if (!CONFIG.includeOpinionFields) {
    return;
  }
  var item = form.addParagraphTextItem();
  item.setTitle('↳ 자유 의견 (선택)');
  if (helpText) {
    item.setHelpText(helpText);
  }
  item.setRequired(false);
}

/**
 * 객관식 문항 + 자유 의견 칸을 추가한다.
 * @param {Form} form 대상 폼
 * @param {string} title 질문
 * @param {string[]} choices 선택지
 * @param {boolean} required 필수 여부
 * @param {string=} opinionHelpText 자유 의견 칸에 붙일 안내문
 * @param {boolean=} withOpinion 자유 의견 칸을 붙일지 (기본 true)
 */
function choice(form, title, choices, required, opinionHelpText, withOpinion) {
  var item = form.addMultipleChoiceItem();
  item.setTitle(title);
  item.setChoiceValues(choices);
  item.setRequired(!!required);

  var attach = (withOpinion === undefined) ? true : withOpinion;
  if (attach) {
    opinion(form, opinionHelpText);
  }
}
