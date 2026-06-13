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
    static final String ExtraDebugCommandScript = "io.github.mesmerprism.questquestionnaire.extra.DEBUG_COMMAND_SCRIPT";
    static final String ExtraDebugCommandIntervalMs = "io.github.mesmerprism.questquestionnaire.extra.DEBUG_COMMAND_INTERVAL_MS";

    static final String QuestionnaireId = "brb-questionnaire-v1";
    static final String StageLanguageSelect = "language_select";
    static final String StageDemographics = "demographics";
    static final String StagePriorExperience = "prior_experience";
    static final String StagePostConditionPictographic = "post_condition:pictographic";
    static final String StagePostConditionPresence = "post_condition:presence_questionnaire";
    static final String StagePostConditionLostOpportunity = "post_condition:lost_opportunity";
    static final String StageFinalEndConfirmation = "final:end_confirmation";
    static final String StageFinalExtraPressesPrompt = "final:extra_presses_prompt";
    static final String StageCompleteExportSummary = "complete:export_summary";
    static final String DefaultStage = StageLanguageSelect;
    static final String ResultAuthority = "org.thebigredbuttoninstitute.app.questionnaire.results";

    static final String[] InitialStudySequence = new String[] {
            StageLanguageSelect,
            StageDemographics,
            StagePriorExperience
    };

    static final String[] ConditionOnePostSequence = new String[] {
            StagePostConditionPictographic,
            StagePostConditionPresence,
            StagePostConditionLostOpportunity
    };

    static final String[] PostConditionSequence = new String[] {
            StagePostConditionPictographic,
            StagePostConditionPresence
    };

    static final String[] FinalSequence = new String[] {
            StageFinalEndConfirmation,
            StageFinalExtraPressesPrompt,
            StageCompleteExportSummary
    };

    private QuestionnaireContract() {
    }
}
