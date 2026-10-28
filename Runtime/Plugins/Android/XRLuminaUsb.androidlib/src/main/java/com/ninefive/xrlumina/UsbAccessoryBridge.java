package com.ninefive.xrlumina;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.hardware.usb.UsbAccessory;
import android.hardware.usb.UsbManager;
import android.os.Build;
import android.os.ParcelFileDescriptor;
import android.util.Log;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.Socket;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/** AOA의 양방향 USB 스트림을 Unity 루프백 소켓에 연결한다. */
public final class UsbAccessoryBridge {
    private final Activity activity;
    private final int port;
    private final UsbManager manager;
    private final String permissionAction;
    private ScheduledExecutorService worker;
    private volatile boolean running;
    private volatile boolean permissionPending;
    private Socket socket;
    private ParcelFileDescriptor descriptor;
    private FileInputStream input;
    private FileOutputStream output;
    private final BroadcastReceiver receiver = new BroadcastReceiver() {
        /** USB 권한 결과를 받아 다음 탐색에서 연결을 재시도한다. */
        @Override
        public void onReceive(Context context, Intent intent) {
            if (permissionAction.equals(intent.getAction())) {
                permissionPending = false;
                denied = !hasAccessoryPermission();
                if (denied) {
                    Log.w("DeviceUsb", "USB 접근이 거부되었습니다. 재연결 후 다시 승인하십시오.");
                }
            }
            if (UsbManager.ACTION_USB_ACCESSORY_DETACHED.equals(intent.getAction())) {
                closeConnection();
                denied = false;
                permissionPending = false;
            }
        }
    };
    private volatile boolean denied;

    /** Unity Activity와 SDK의 로컬 수신 포트를 저장한다. */
    public UsbAccessoryBridge(Activity activity, int port) {
        this.activity = activity;
        this.port = port;
        this.manager = (UsbManager) activity.getSystemService(Context.USB_SERVICE);
        this.permissionAction = activity.getPackageName() + ".XRLUMINA_USB_PERMISSION";
    }

    /** 콜백 부가 정보 대신 현재 연결된 SDK 액세서리의 실제 시스템 권한을 확인한다. */
    private boolean hasAccessoryPermission() {
        UsbAccessory[] accessories = manager.getAccessoryList();
        if (accessories != null) {
            for (UsbAccessory accessory : accessories) {
                if ("NineFive".equals(accessory.getManufacturer()) && "XRLumina".equals(accessory.getModel())
                    && manager.hasPermission(accessory)) {
                    return true;
                }
            }
        }
        return false;
    }

    /** 권한 브로드캐스트와 AOA 장치 주기 탐색을 시작한다. */
    public synchronized void start() {
        if (running) {
            return;
        }
        IntentFilter filter = new IntentFilter(permissionAction);
        filter.addAction(UsbManager.ACTION_USB_ACCESSORY_DETACHED);
        if (Build.VERSION.SDK_INT >= 33) {
            activity.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED);
        } else {
            activity.registerReceiver(receiver, filter);
        }
        running = true;
        worker = Executors.newSingleThreadScheduledExecutor();
        worker.scheduleWithFixedDelay(this::connectAccessory, 0, 2, TimeUnit.SECONDS);
    }

    /** SDK가 실행 중일 때 이 제품의 AOA 장치만 승인 요청 후 연결한다. */
    private synchronized void connectAccessory() {
        if (!running || descriptor != null || permissionPending) {
            return;
        }
        try {
            UsbAccessory[] accessories = manager.getAccessoryList();
            if (accessories == null) {
                return;
            }
            for (UsbAccessory accessory : accessories) {
                if (!"NineFive".equals(accessory.getManufacturer()) || !"XRLumina".equals(accessory.getModel())) {
                    continue;
                }
                if (!manager.hasPermission(accessory)) {
                    if (denied) {
                        return;
                    }
                    permissionPending = true;
                    int permissionFlags = PendingIntent.FLAG_UPDATE_CURRENT;
                    if (Build.VERSION.SDK_INT >= 31) {
                        permissionFlags |= PendingIntent.FLAG_MUTABLE;
                    }
                    PendingIntent permission = PendingIntent.getBroadcast(activity, 0,
                        new Intent(permissionAction).setPackage(activity.getPackageName()),
                        permissionFlags);
                    manager.requestPermission(accessory, permission);
                    return;
                }
                denied = false;
                // C# UsbConnectionListener의 IPAddress.Loopback(IPv4)과 주소 체계를 맞춘다.
                Socket nextSocket = new Socket(InetAddress.getByAddress(new byte[] { 127, 0, 0, 1 }), port);
                nextSocket.setTcpNoDelay(true);
                ParcelFileDescriptor nextDescriptor;
                try {
                    nextDescriptor = manager.openAccessory(accessory);
                    if (nextDescriptor == null) {
                        nextSocket.close();
                        return;
                    }
                } catch (Exception error) {
                    nextSocket.close();
                    throw error;
                }
                socket = nextSocket;
                descriptor = nextDescriptor;
                input = new FileInputStream(descriptor.getFileDescriptor());
                output = new FileOutputStream(descriptor.getFileDescriptor());
                final FileInputStream usbInput = input;
                final FileOutputStream usbOutput = output;
                Thread incoming = new Thread(() -> copy(usbInput, nextSocket, true), "XRLumina-USB-in");
                Thread outgoing = new Thread(() -> copy(usbOutput, nextSocket, false), "XRLumina-USB-out");
                incoming.setDaemon(true);
                outgoing.setDaemon(true);
                incoming.start();
                outgoing.start();
                Log.i("DeviceUsb", "AOA USB와 SDK 소켓 연결 완료");
                return;
            }
        } catch (Exception error) {
            closeConnection();
            Log.w("DeviceUsb", "AOA 연결 실패", error);
        }
    }

    /** USB 수신 또는 송신 스트림을 로컬 소켓과 순서대로 중계한다. */
    private void copy(java.io.Closeable usb, Socket connection, boolean incoming) {
        try {
            InputStream source = incoming ? (InputStream) usb : connection.getInputStream();
            OutputStream target = incoming ? connection.getOutputStream() : (OutputStream) usb;
            byte[] buffer = new byte[16384];
            int count;
            while (running && (count = source.read(buffer)) != -1) {
                target.write(buffer, 0, count);
            }
        } catch (Exception error) {
            if (running && !connection.isClosed()) {
                Log.w("DeviceUsb", "AOA 연결 종료", error);
            }
        } finally {
            synchronized (this) {
                if (socket == connection) {
                    closeConnection();
                }
            }
        }
    }

    /** 양방향 중계를 중지하여 수신 대기 스레드를 해제한다. */
    private synchronized void closeConnection() {
        java.io.Closeable[] resources = { socket, input, output, descriptor };
        socket = null;
        input = null;
        output = null;
        descriptor = null;
        for (java.io.Closeable resource : resources) {
            if (resource != null) {
                try {
                    resource.close();
                } catch (Exception ignored) { }
            }
        }
    }

    /** Unity 종료 시 탐색과 브로드캐스트 구독 및 USB 연결을 정리한다. */
    public synchronized void stop() {
        if (!running) {
            return;
        }
        running = false;
        worker.shutdownNow();
        activity.unregisterReceiver(receiver);
        closeConnection();
    }
}
