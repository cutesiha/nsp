namespace NSP.Dialogue;

// 증거 기반 심문의 답변 표현.
//
// Local Dialogue V2 에서 문장 틀은 전부 data/dialogue/lines/*.txt 로 옮겼고, 조립은
// DialogueComposer 한 곳에서 한다. 이 클래스는 예전 호출부(InterviewReplyPlanner 등)를
// 위해 이름만 남은 얇은 입구다.
//
// 슬롯 이름은 "<주제>.<결>" — 예: MoveReason.ordered, Companion.alone, WhereAtIncident.any
// 찾는 순서는 직원 id → 말투 공통(fml/sft) → any.
public static class InterviewReplyComposer
{
    public static string Compose(ReplyFrame frame) => DialogueComposer.Compose(frame);
}
