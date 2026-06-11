package org.thebigredbuttoninstitute.questionnaire;

import android.content.Context;
import android.content.SharedPreferences;
import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

final class QuestionnaireResultStore {
    private static final String Prefs = "questionnaire-caller";
    private static final String LatestRequestId = "latest_request_id";
    private static final String LatestNonce = "latest_nonce";
    private static final String LatestQuestionnaireId = "latest_questionnaire_id";
    private static final String LatestStage = "latest_stage";
    private static final String LatestScreenSequence = "latest_screen_sequence";
    private static final String LatestResultPath = "latest_result_path";
    private static final String LatestCallbackRequestId = "latest_callback_request_id";
    private static final String LastResumeCheckAt = "last_resume_check_at";
    private static final long MaxResultBytes = 1024L * 1024L;

    private QuestionnaireResultStore() {
    }

    static void rememberPending(
            Context context,
            String requestId,
            String nonce,
            String questionnaireId,
            String stage,
            String[] screenSequence,
            String resultPath) {
        prefs(context)
                .edit()
                .putString(LatestRequestId, requestId)
                .putString(LatestNonce, nonce)
                .putString(LatestQuestionnaireId, questionnaireId)
                .putString(LatestStage, stage)
                .putString(LatestScreenSequence, join(screenSequence))
                .putString(LatestResultPath, resultPath)
                .remove(LatestCallbackRequestId)
                .apply();
    }

    static void markCallback(Context context, String requestId) {
        if (requestId == null || requestId.trim().isEmpty()) {
            return;
        }

        SharedPreferences preferences = prefs(context);
        String latestRequestId = preferences.getString(LatestRequestId, null);
        if (!requestId.equals(latestRequestId)) {
            return;
        }

        preferences.edit().putString(LatestCallbackRequestId, requestId).apply();
    }

    static void markResumeCheck(Context context) {
        prefs(context).edit().putLong(LastResumeCheckAt, System.currentTimeMillis()).apply();
    }

    static String readLatestResultSummary(Context context) {
        SharedPreferences preferences = prefs(context);
        String requestId = preferences.getString(LatestRequestId, null);
        if (requestId == null || requestId.trim().isEmpty()) {
            return "No request launched yet.";
        }

        String nonce = preferences.getString(LatestNonce, "");
        String questionnaireId = preferences.getString(LatestQuestionnaireId, QuestionnaireContract.QuestionnaireId);
        String stage = preferences.getString(LatestStage, QuestionnaireContract.DefaultStage);
        String[] screenSequence = split(preferences.getString(LatestScreenSequence, stage));
        String resultPath = preferences.getString(LatestResultPath, "");
        String callbackRequestId = preferences.getString(LatestCallbackRequestId, "");
        String callbackState = requestId.equals(callbackRequestId) ? "received" : "pending";

        File file = new File(resultPath != null ? resultPath : "");
        if (!file.exists()) {
            return "Pending request. No result file yet. Callback: " + callbackState + ".";
        }

        long byteCount = file.length();
        if (byteCount > MaxResultBytes) {
            return "Result invalid reason=oversized_result callback=" + callbackState + " bytes=" + byteCount + ".";
        }

        try {
            String resultJson = new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
            ExpectedQuestionnaireResult expected = new ExpectedQuestionnaireResult(
                    requestId,
                    nonce,
                    questionnaireId,
                    stage,
                    screenSequence);
            QuestionnaireResultValidation validation = QuestionnaireResultValidator.validate(resultJson, expected);
            if (validation.valid) {
                return "Result status=" + validation.status + " valid=true callback=" + callbackState + " bytes=" + byteCount + ".";
            }

            return "Result invalid reason=" + validation.reason + " callback=" + callbackState + " bytes=" + byteCount + ".";
        } catch (IOException ex) {
            return "Result invalid reason=result_read_failed callback=" + callbackState + " bytes=" + byteCount + ".";
        }
    }

    private static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences(Prefs, Context.MODE_PRIVATE);
    }

    private static String join(String[] values) {
        if (values == null || values.length == 0) {
            return "";
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < values.length; i++) {
            if (values[i] == null || values[i].trim().isEmpty()) {
                continue;
            }

            if (builder.length() > 0) {
                builder.append('\n');
            }

            builder.append(values[i]);
        }

        return builder.toString();
    }

    private static String[] split(String value) {
        if (value == null || value.trim().isEmpty()) {
            return new String[0];
        }

        return value.split("\\n");
    }
}
