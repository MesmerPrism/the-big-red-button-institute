package org.thebigredbuttoninstitute.questionnaire;

import java.util.Arrays;
import java.util.HashSet;
import java.util.Set;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

final class ExpectedQuestionnaireResult {
    final String requestId;
    final String nonce;
    final String questionnaireId;
    final String stage;
    final String[] screenSequence;

    ExpectedQuestionnaireResult(
            String requestId,
            String nonce,
            String questionnaireId,
            String stage,
            String[] screenSequence) {
        this.requestId = requestId;
        this.nonce = nonce;
        this.questionnaireId = questionnaireId;
        this.stage = stage;
        this.screenSequence = screenSequence != null ? screenSequence.clone() : new String[0];
    }
}

final class QuestionnaireResultValidation {
    final boolean valid;
    final String status;
    final String reason;

    private QuestionnaireResultValidation(boolean valid, String status, String reason) {
        this.valid = valid;
        this.status = status;
        this.reason = reason;
    }

    static QuestionnaireResultValidation valid(String status) {
        return new QuestionnaireResultValidation(true, status, "");
    }

    static QuestionnaireResultValidation invalid(String reason) {
        return new QuestionnaireResultValidation(false, "", reason);
    }
}

final class QuestionnaireResultValidator {
    private static final Set<String> KnownStatuses = new HashSet<>(Arrays.asList("completed", "cancelled", "error"));
    private static final Set<String> PlaceholderAnswers = new HashSet<>(Arrays.asList("yes", "no", "not_answered"));

    private QuestionnaireResultValidator() {
    }

    static QuestionnaireResultValidation validate(String resultJson, ExpectedQuestionnaireResult expected) {
        JSONObject json;
        try {
            json = new JSONObject(resultJson);
        } catch (JSONException ex) {
            return QuestionnaireResultValidation.invalid("malformed_result_json");
        }

        String status = json.optString("status");
        if (!QuestionnaireContract.ProtocolVersion.equals(json.optString("protocol_version"))) {
            return QuestionnaireResultValidation.invalid("unsupported_protocol");
        }

        if (!QuestionnaireContract.ResultSchema.equals(json.optString("schema"))) {
            return QuestionnaireResultValidation.invalid("unsupported_schema");
        }

        if (!expected.requestId.equals(json.optString("request_id"))) {
            return QuestionnaireResultValidation.invalid("request_id_mismatch");
        }

        if (!expected.nonce.equals(json.optString("nonce"))) {
            return QuestionnaireResultValidation.invalid("nonce_mismatch");
        }

        if (!KnownStatuses.contains(status)) {
            return QuestionnaireResultValidation.invalid("unknown_status");
        }

        if (!expected.stage.equals(json.optString("stage"))) {
            return QuestionnaireResultValidation.invalid("stage_mismatch");
        }

        if (!Arrays.equals(expected.screenSequence, toStringArray(json.optJSONArray("screen_sequence")))) {
            return QuestionnaireResultValidation.invalid("screen_sequence_mismatch");
        }

        if (!questionnaireMatches(json.optJSONObject("questionnaire"), expected.questionnaireId)) {
            return QuestionnaireResultValidation.invalid("questionnaire_mismatch");
        }

        return validateAnswers(status, expected.stage, json.optJSONObject("answers"));
    }

    private static QuestionnaireResultValidation validateAnswers(String status, String expectedStage, JSONObject answers) {
        if (answers == null) {
            return QuestionnaireResultValidation.invalid("missing_answers");
        }

        if (!"completed".equals(status)) {
            return QuestionnaireResultValidation.valid(status);
        }

        if (!expectedStage.equals(answers.optString("open_stage"))) {
            return QuestionnaireResultValidation.invalid("answer_stage_mismatch");
        }

        String placeholderAnswer = answers.optString("placeholder_answer");
        if (!PlaceholderAnswers.contains(placeholderAnswer)) {
            return QuestionnaireResultValidation.invalid("invalid_placeholder_answer");
        }

        return QuestionnaireResultValidation.valid(status);
    }

    private static boolean questionnaireMatches(JSONObject questionnaire, String expectedQuestionnaireId) {
        return questionnaire != null
                && expectedQuestionnaireId.equals(questionnaire.optString("id"))
                && questionnaire.optInt("version") >= 1;
    }

    private static String[] toStringArray(JSONArray array) {
        if (array == null) {
            return new String[0];
        }

        String[] values = new String[array.length()];
        for (int i = 0; i < array.length(); i++) {
            values[i] = array.optString(i);
        }

        return values;
    }
}
