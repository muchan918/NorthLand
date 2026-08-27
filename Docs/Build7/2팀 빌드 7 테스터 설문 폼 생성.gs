/**
 * 2팀 빌드 7 테스터 설문 — 구글 폼 생성 스크립트
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
 * ── 문항 구성 ─────────────────────────────────────────────
 * 대분류 3개(비주얼 · 밸런스 · 튜토리얼) + 마지막 자유 의견.
 * 문항 17개, 각 문항마다 자유 의견(장문) 칸이 붙는다.
 * 정본은 `2팀 빌드 7 테스터 설문.md` — 문항을 고치면 양쪽을 같이 고칠 것.
 * ─────────────────────────────────────────────────────────
 */

var CONFIG = {
  title: '2팀 빌드 7 테스터 설문',
  description: [
    'NorthLand: Last Stand — Build 7 테스터 설문입니다.',
    '',
    '버그를 찾아달라는 설문이 아닙니다. 재미있었는지, 무엇을 개선하면 좋을지를 알고 싶습니다.',
    '',
    '[플레이 안내]',
    '· zip 압축을 풀고 exe 실행 → 타이틀에서 세이브 슬롯을 하나 고른 뒤 "게임 시작"',
    '· 처음 시작하면 튜토리얼이 진행됩니다',
    '· 카메라: WASD 또는 우클릭 드래그로 이동, 휠로 줌 / ESC로 설정(언어 · 해상도 · 볼륨)',
    '· 한 판은 15웨이브입니다. 끝까지 못 가도 괜찮습니다.',
    '',
    '설문은 5~10분 정도 걸립니다. "자유 의견" 칸은 비워도 됩니다.'
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

  // ── 비주얼 ──────────────────────────────────────────────
  section(form, '비주얼', '화면에서 정보가 잘 읽혔는지, 무엇을 다듬으면 좋을지 알고 싶습니다.');

  choice(form,
    '1. 경영 공간 건물 패널에서 수치 정보(생산량 · 강화 효과의 다음 값)를 초록색으로 표시하고 있습니다. 잘 보였나요?',
    [
      '잘 보였다 — 지금 색이 좋다',
      '보이긴 했지만 다른 색이 나을 것 같다',
      '잘 안 보였다',
      '초록색으로 표시되는 줄 몰랐다'
    ],
    true,
    '다른 색이 낫다면 어떤 색이 좋을지, 배경과 겹쳐 안 보였던 곳이 있었는지 적어주세요.');

  choice(form,
    '2. 타워 합성 패널에서 선택한 타워들이 이름 목록 형태로 스크롤 뷰에 표시됩니다. 이 방식이 괜찮았나요?',
    [
      '괜찮았다 — 뭘 골랐는지 알아보기 쉬웠다',
      '알아보기는 했지만 불편했다',
      '이름만으로는 어떤 타워인지 바로 안 떠올랐다',
      '합성 패널을 거의 안 썼다'
    ],
    true,
    '별로였다면 어떤 방식이 좋을지 적어주세요. (아이콘으로 표시 · 아이콘 + 이름 · 선택한 타워를 맵에서 강조 등)');

  choice(form,
    '3. 도감 패널에서 타워 정보(이름 · 역할 · 설명 · 능력치 · 합성 재료)를 잘 볼 수 있었나요?',
    [
      '필요한 정보를 다 찾을 수 있었다',
      '일부는 찾기 어려웠다',
      '정보가 많아 한눈에 안 들어왔다',
      '도감을 안 열어봤다'
    ],
    true,
    '찾기 어려웠던 정보, 글자 크기 · 배치 · 스크롤에서 불편했던 점을 적어주세요.');

  checkbox(form,
    '4. 스킬 보상 패널(웨이브를 깨고 보상 3개 중 하나를 고르는 화면)에서 개선했으면 하는 점이 있나요? (여러 개 선택 가능)',
    [
      '카드가 무엇을 주는지 잘 안 읽힌다',
      '등급(별 · 색) 구분이 잘 안 된다',
      '지금 내 스킬 레벨과 비교가 안 된다',
      '연출 · 이펙트가 밋밋하다',
      '특별히 없다'
    ],
    true,
    '어떻게 바뀌면 좋을지 적어주세요.');

  checkbox(form,
    '5. 그 외에 개선했으면 하는 비주얼 요소가 있나요? (UI · 파티클 위주 · 여러 개 선택 가능)',
    [
      'UI (패널 · 버튼 · 글자)',
      '파티클 · 이펙트 (공격 · 스킬 · 건물)',
      '몬스터 · 타워 모델',
      '맵 · 배경',
      '특별히 없다'
    ],
    true,
    '어느 부분이 어떻게 바뀌면 좋을지 적어주세요.');

  choice(form,
    '6. 버프 타일에 타워를 놓을 때, 공격력 · 사거리가 얼마나 늘어나는지 수치로 표시해주면 좋을까요?',
    [
      '수치로 보여주면 좋겠다',
      '지금처럼 아이콘만 있어도 충분하다',
      '수치까지는 필요 없고 강해진다는 정도만 알면 된다',
      '버프 타일을 신경 쓰지 않았다'
    ],
    true,
    '수치를 보여준다면 어디에 뜨는 게 좋을지 적어주세요. (타일 위 · 타워 정보 패널 · 배치 미리보기 옆 등)');

  // ── 밸런스 ──────────────────────────────────────────────
  section(form, '밸런스', '난이도와 자원 흐름이 어떻게 느껴졌는지 알고 싶습니다.');

  choice(form,
    '7. 어떤 전략으로 플레이했나요?',
    [
      '타워를 많이 짓는 쪽',
      '타워 합성 위주',
      '주민 수 늘리기 위주',
      '생산 건물 업그레이드 위주',
      '특별한 전략 없이 그때그때'
    ],
    true,
    '그 전략을 고른 이유, 어디서 · 무엇 때문에 막혔는지 적어주세요.');

  text(form, '7-1. 최대 몇 웨이브까지 갔나요? (숫자로 적어주세요 · 한 판은 15웨이브입니다)', true);

  choice(form,
    '8. 타워 합성의 장점이 느껴졌나요?',
    [
      '확실히 이득이라 자주 썼다',
      '이득인 것 같아서 가끔 썼다',
      '재료로 쓴 타워가 아까워서 잘 안 썼다',
      '이득인지 아닌지 모르겠다',
      '합성을 안 해봤다'
    ],
    true,
    '합성 타워가 재료 타워 여러 개보다 나았는지, 손해처럼 느껴진 순간이 있었는지 적어주세요.');

  checkbox(form,
    '9. 자원이 부족했나요, 남았나요? (여러 개 선택 가능)',
    [
      '비스켓이 늘 부족했다',
      '초코가 늘 부족했다',
      '설탕이 늘 부족했다',
      '남아도는 자원이 있었다',
      '전체적으로 적당했다'
    ],
    true,
    '어느 자원이 남았고 어느 자원이 모자랐는지 적어주세요.');

  choice(form,
    '10. 자원이 모일 때까지 기다린다는 느낌이 들었나요?',
    [
      '거의 없었다 — 항상 할 일이 있었다',
      '가끔 기다렸다',
      '자주 기다렸다 — 낮에 할 게 없었다',
      '오히려 자원이 넘쳐서 고민할 게 없었다'
    ],
    true,
    '몇 웨이브쯤부터 그렇게 느꼈는지 적어주세요.');

  choice(form,
    '11. 설탕 · 마나석처럼 쓸 곳이 마땅치 않다고 느낀 자원이 있었나요?',
    [
      '있었다',
      '없었다 — 다 쓸 데가 있었다',
      '어디에 쓰는지 몰랐다'
    ],
    true,
    '어떤 자원이었는지, 무엇에 쓸 수 있으면 좋겠는지 적어주세요.');

  choice(form,
    '12. 타워가 웨이브 진행에 따라 하나씩 해금됩니다. 열리는 시점이 적당했나요?',
    [
      '적당했다',
      '너무 늦게 열려서 답답했다',
      '너무 빨리 다 열려서 고민할 게 없었다',
      '해금되는 줄 몰랐다'
    ],
    true,
    '어떤 타워를 더 빨리 / 더 늦게 열었으면 좋겠는지 적어주세요.');

  choice(form,
    '13. 생산 건물 업그레이드를 어디까지 해봤나요?',
    [
      '최대 레벨까지 올려봤다',
      '중간까지 올렸다',
      '한두 번만 올렸다',
      '거의 안 올렸다'
    ],
    true,
    '업그레이드에 자원을 쓰는 게 이득으로 느껴졌는지, 타워를 짓는 것과 비교해 어땠는지 적어주세요.');

  // ── 튜토리얼 ────────────────────────────────────────────
  section(form, '튜토리얼', '이번 빌드에 새로 들어간 튜토리얼에 대한 문항입니다.');

  choice(form,
    '14. 튜토리얼 길이가 어땠나요?',
    [
      '적당했다',
      '조금 길었다',
      '너무 길어서 지쳤다',
      '짧아서 더 알려줬으면 했다',
      '중간에 건너뛰었다'
    ],
    true,
    '길었다면 어느 부분부터 늘어진다고 느꼈는지 적어주세요.');

  choice(form,
    '15. 튜토리얼에서 이해가 안 되는 부분이 있었나요?',
    [
      '없었다 — 다 이해했다',
      '일부 이해가 안 됐다',
      '설명은 읽었지만 왜 그렇게 해야 하는지 몰랐다',
      '튜토리얼이 끝나고도 뭘 해야 할지 몰랐다'
    ],
    true,
    '어느 단계에서 막혔는지, 어떤 설명이 더 있으면 좋았을지 적어주세요.');

  choice(form,
    '16. 튜토리얼 중에는 진행에 필요한 조작만 열려 있고 나머지(버튼 클릭 · 건물 선택 · 단축키 · 타워 선택 등)는 막혀 있습니다. 불편했나요?',
    [
      '괜찮았다 — 헤매지 않아 좋았다',
      '답답했지만 참을 만했다',
      '답답했다 — 다른 것도 만져보고 싶었다',
      '막혀 있는 줄 모르고 눌렀다가 반응이 없어 당황했다'
    ],
    true,
    '어떤 조작이 막혀서 답답했는지, 어디까지 열어주면 좋을지 적어주세요.');

  // ── 자유 의견 ───────────────────────────────────────────
  section(form, '자유 의견', '');

  form.addParagraphTextItem()
    .setTitle('17. 마지막으로, 자유롭게 하고 싶은 말을 적어주세요.')
    .setHelpText([
      '위 문항에 없던 내용도 좋습니다. 이런 것들이 특히 궁금합니다.',
      '· 가장 재미있었던 순간과 가장 지루했던 순간',
      '· 한 판이 끝나고 또 하고 싶었는지',
      '· 딱 하나만 고친다면 무엇을 고쳤으면 하는지'
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

/** 대분류 구분 머리말을 추가한다. */
function section(form, title, description) {
  var item = form.addSectionHeaderItem().setTitle(title);
  if (description) {
    item.setHelpText(description);
  }
}

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
 * 객관식(하나만 선택) 문항 + 자유 의견 칸을 추가한다.
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

/**
 * 복수 선택 문항 + 자유 의견 칸을 추가한다.
 * @param {Form} form 대상 폼
 * @param {string} title 질문
 * @param {string[]} choices 선택지
 * @param {boolean} required 필수 여부
 * @param {string=} opinionHelpText 자유 의견 칸에 붙일 안내문
 * @param {boolean=} withOpinion 자유 의견 칸을 붙일지 (기본 true)
 */
function checkbox(form, title, choices, required, opinionHelpText, withOpinion) {
  var item = form.addCheckboxItem();
  item.setTitle(title);
  item.setChoiceValues(choices);
  item.setRequired(!!required);

  var attach = (withOpinion === undefined) ? true : withOpinion;
  if (attach) {
    opinion(form, opinionHelpText);
  }
}
