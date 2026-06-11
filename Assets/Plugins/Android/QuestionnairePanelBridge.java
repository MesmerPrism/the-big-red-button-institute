package org.thebigredbuttoninstitute.questionnaire;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.ActivityNotFoundException;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.net.Uri;
import java.io.File;
import java.io.IOException;
import java.util.UUID;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

public final class QuestionnairePanelBridge {
    private static final String BrbLaunchExtraOpen = "brb.questionnaireOpen";
    private static final String BrbLaunchExtraDebugAutoSubmit = "brb.questionnaireDebugAutoSubmit";
    private static final int LaunchExtraOpenBit = 1;
    private static final int LaunchExtraDebugAutoSubmitBit = 2;

    private QuestionnairePanelBridge() {
    }

    public static String launchDemographics(Activity activity) {
        return launchDemographics(activity, false);
    }

    public static String launchDemographics(Activity activity, boolean debugAutoSubmit) {
        return launch(activity, QuestionnaireContract.DefaultStage, debugAutoSubmit);
    }

    public static int consumeQuestionnaireLaunchExtra(Activity activity) {
        if (activity == null || activity.getIntent() == null) {
            return 0;
        }

        Intent intent = activity.getIntent();
        boolean open = intent.getBooleanExtra(BrbLaunchExtraOpen, false);
        boolean debugAutoSubmit = intent.getBooleanExtra(BrbLaunchExtraDebugAutoSubmit, false);
        if (open || debugAutoSubmit) {
            intent.removeExtra(BrbLaunchExtraOpen);
            intent.removeExtra(BrbLaunchExtraDebugAutoSubmit);
        }

        if (!open) {
            return 0;
        }

        return LaunchExtraOpenBit | (debugAutoSubmit ? LaunchExtraDebugAutoSubmitBit : 0);
    }

    public static String readLatestResultSummary(Activity activity) {
        if (activity == null) {
            return "Questionnaire result unavailable: missing Unity activity.";
        }

        QuestionnaireResultStore.markResumeCheck(activity);
        return QuestionnaireResultStore.readLatestResultSummary(activity);
    }

    private static String launch(Activity activity, String stage, boolean debugAutoSubmit) {
        if (activity == null) {
            return "questionnaire_open failed: missing Unity activity.";
        }

        if (!QuestionnaireContract.DefaultStage.equals(stage)) {
            return "questionnaire_open failed: unsupported stage.";
        }

        String requestId = UUID.randomUUID().toString();
        String nonce = UUID.randomUUID().toString();
        String sessionId = "brb-unity-" + System.currentTimeMillis();
        String[] screenSequence = new String[] { stage };

        try {
            File resultFile = QuestionnaireResultProvider.prepareResultFile(activity, requestId);
            Uri resultUri = QuestionnaireResultProvider.resultUri(requestId);
            PendingIntent returnToCaller = buildReturnPendingIntent(activity, requestId);
            JSONObject requestJson = buildRequestJson(activity, sessionId, requestId, nonce, stage, screenSequence);

            QuestionnaireResultStore.rememberPending(
                    activity,
                    requestId,
                    nonce,
                    QuestionnaireContract.QuestionnaireId,
                    stage,
                    screenSequence,
                    resultFile.getAbsolutePath());

            Intent intent = new Intent(QuestionnaireContract.StartAction);
            intent.setComponent(new ComponentName(QuestionnaireContract.PanelPackage, QuestionnaireContract.PanelActivity));
            intent.addCategory(Intent.CATEGORY_DEFAULT);
            intent.setDataAndType(resultUri, QuestionnaireContract.RequestMimeType);
            intent.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            intent.putExtra(QuestionnaireContract.ExtraSessionId, sessionId);
            intent.putExtra(QuestionnaireContract.ExtraRequestId, requestId);
            intent.putExtra(QuestionnaireContract.ExtraNonce, nonce);
            intent.putExtra(QuestionnaireContract.ExtraRequestJson, requestJson.toString());
            intent.putExtra(QuestionnaireContract.ExtraResultUri, resultUri);
            intent.putExtra(QuestionnaireContract.ExtraReturnToCaller, returnToCaller);
            if (debugAutoSubmit) {
                intent.putExtra(QuestionnaireContract.ExtraDebugAutoSubmit, true);
            }

            activity.startActivity(intent);
            return "Launched questionnaire panel stage=" + stage + " request=" + shortId(requestId) + ".";
        } catch (ActivityNotFoundException ex) {
            return "questionnaire_open failed: panel app not installed.";
        } catch (IOException ex) {
            return "questionnaire_open failed: result URI setup failed.";
        } catch (JSONException ex) {
            return "questionnaire_open failed: request JSON setup failed.";
        } catch (RuntimeException ex) {
            return "questionnaire_open failed: " + safeMessage(ex);
        }
    }

    private static PendingIntent buildReturnPendingIntent(Activity activity, String requestId) {
        Intent completionIntent = new Intent(activity, QuestionnaireReturnReceiver.class);
        completionIntent.setAction(QuestionnaireContract.CompleteAction);
        completionIntent.setData(Uri.parse("app://" + activity.getPackageName() + "/questionnaire-return/" + requestId));
        completionIntent.putExtra(QuestionnaireContract.ExtraRequestId, requestId);

        return PendingIntent.getBroadcast(
                activity,
                requestId.hashCode(),
                completionIntent,
                PendingIntent.FLAG_CANCEL_CURRENT
                        | PendingIntent.FLAG_ONE_SHOT
                        | PendingIntent.FLAG_IMMUTABLE);
    }

    private static JSONObject buildRequestJson(
            Activity activity,
            String sessionId,
            String requestId,
            String nonce,
            String stage,
            String[] screenSequence) throws JSONException {
        JSONArray sequenceJson = new JSONArray();
        for (String screen : screenSequence) {
            sequenceJson.put(screen);
        }

        JSONObject callerJson = new JSONObject()
                .put("package", activity.getPackageName())
                .put("app_version", resolveAppVersion(activity))
                .put("engine", "unity");

        return new JSONObject()
                .put("protocol_version", QuestionnaireContract.ProtocolVersion)
                .put("session_id", sessionId)
                .put("request_id", requestId)
                .put("nonce", nonce)
                .put("study_id", "brb")
                .put("schema_id", QuestionnaireContract.QuestionnaireId)
                .put("open_stage", stage)
                .put("condition_number", JSONObject.NULL)
                .put("screen_sequence", sequenceJson)
                .put("caller", callerJson);
    }

    private static String resolveAppVersion(Activity activity) {
        try {
            PackageInfo packageInfo = activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0);
            return packageInfo.versionName != null ? packageInfo.versionName : "";
        } catch (RuntimeException ex) {
            return "";
        } catch (Exception ex) {
            return "";
        }
    }

    private static String shortId(String requestId) {
        return requestId.length() <= 8 ? requestId : requestId.substring(0, 8);
    }

    private static String safeMessage(RuntimeException ex) {
        String message = ex.getMessage();
        if (message == null || message.trim().isEmpty()) {
            return ex.getClass().getSimpleName();
        }

        return message;
    }
}
