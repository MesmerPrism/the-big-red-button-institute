package org.thebigredbuttoninstitute.questionnaire;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import java.io.File;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.util.List;

public final class QuestionnaireResultProvider extends ContentProvider {
    private static final String ResultRoot = "questionnaire-results";
    private static final String ResultFileName = "result.json";

    public static Uri resultUri(String requestId) {
        return new Uri.Builder()
                .scheme("content")
                .authority(QuestionnaireContract.ResultAuthority)
                .appendPath("result")
                .appendPath(requestId)
                .build();
    }

    public static File prepareResultFile(Context context, String requestId) throws IOException {
        File file = resultFile(context, requestId);
        File parent = file.getParentFile();
        if (parent == null || (!parent.exists() && !parent.mkdirs())) {
            throw new IOException("Could not create result directory.");
        }

        if (file.exists() && !file.delete()) {
            throw new IOException("Could not reset result file.");
        }

        return file;
    }

    public static File resultFile(Context context, String requestId) throws FileNotFoundException {
        if (!isValidRequestId(requestId)) {
            throw new FileNotFoundException("Invalid request id.");
        }

        return new File(new File(new File(context.getFilesDir(), ResultRoot), requestId), ResultFileName);
    }

    @Override
    public boolean onCreate() {
        return true;
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        Context context = getContext();
        if (context == null) {
            throw new FileNotFoundException("Provider context unavailable.");
        }

        String requestId = requestIdFromUri(context, uri);
        File file = resultFile(context, requestId);
        if (mode != null && isWriteMode(mode)) {
            File parent = file.getParentFile();
            if (parent == null || (!parent.exists() && !parent.mkdirs())) {
                throw new FileNotFoundException("Could not create result directory.");
            }
        }

        try {
            return ParcelFileDescriptor.open(file, ParcelFileDescriptor.parseMode(mode != null ? mode : "r"));
        } catch (IllegalArgumentException ex) {
            throw new FileNotFoundException("Unsupported file mode.");
        }
    }

    @Override
    public String getType(Uri uri) {
        Context context = getContext();
        if (context == null) {
            return null;
        }

        try {
            requestIdFromUri(context, uri);
            return "application/json";
        } catch (FileNotFoundException ex) {
            return null;
        }
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        return null;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        throw new UnsupportedOperationException("Questionnaire result provider is file-only.");
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException("Questionnaire result provider is file-only.");
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException("Questionnaire result provider is file-only.");
    }

    private static String requestIdFromUri(Context context, Uri uri) throws FileNotFoundException {
        if (uri == null || !"content".equals(uri.getScheme())) {
            throw new FileNotFoundException("Unsupported URI scheme.");
        }

        if (!QuestionnaireContract.ResultAuthority.equals(uri.getAuthority())) {
            throw new FileNotFoundException("Unsupported URI authority.");
        }

        List<String> segments = uri.getPathSegments();
        if (segments.size() != 2 || !"result".equals(segments.get(0))) {
            throw new FileNotFoundException("Unsupported result path.");
        }

        String requestId = segments.get(1);
        if (!isValidRequestId(requestId)) {
            throw new FileNotFoundException("Invalid request id.");
        }

        return requestId;
    }

    private static boolean isWriteMode(String mode) {
        return mode.indexOf('w') >= 0 || mode.indexOf('a') >= 0 || mode.indexOf('t') >= 0;
    }

    private static boolean isValidRequestId(String requestId) {
        if (requestId == null || requestId.isEmpty() || requestId.length() > 96) {
            return false;
        }

        for (int i = 0; i < requestId.length(); i++) {
            char value = requestId.charAt(i);
            boolean allowed = (value >= 'a' && value <= 'z')
                    || (value >= 'A' && value <= 'Z')
                    || (value >= '0' && value <= '9')
                    || value == '-'
                    || value == '_'
                    || value == '.';
            if (!allowed) {
                return false;
            }
        }

        return true;
    }
}
