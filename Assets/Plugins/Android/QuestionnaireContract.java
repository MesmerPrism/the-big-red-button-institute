package org.thebigredbuttoninstitute.questionnaire;

final class QuestionnaireContract {
    static final String ProtocolVersion = "quest.questionnaire.v1";
    static final String ResultSchema = "quest.questionnaire.v1.result";
    static final String StartAction = "io.github.mesmerprism.questquestionnaire.action.START";
    static final String CompleteAction = "io.github.mesmerprism.questquestionnaire.action.COMPLETE";
    static final String RequestMimeType = "application/vnd.quest-questionnaire.request+json";

    static final String PanelPackage = "io.github.mesmerprism.questquestionnaire.panel";
    static final String PanelActivity = "io.github.mesmerprism.questquestionnaire.panel.QuestionnaireActivity";

    static final String ExtraSessionId = "session_id";
    static final String ExtraRequestId = "request_id";
    static final String ExtraNonce = "request_nonce";
    static final String ExtraRequestJson = "request_json";
    static final String ExtraResultUri = "result_uri";
    static final String ExtraReturnToCaller = "return_to_caller";
    static final String ExtraDebugAutoSubmit = "io.github.mesmerprism.questquestionnaire.extra.DEBUG_AUTO_SUBMIT";

    static final String QuestionnaireId = "brb-questionnaire-v1";
    static final String DefaultStage = "demographics";
    static final String ResultAuthority = "org.thebigredbuttoninstitute.app.questionnaire.results";

    private QuestionnaireContract() {
    }
}
