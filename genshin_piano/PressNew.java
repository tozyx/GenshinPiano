package genshin_piano;

import java.awt.*;
import java.util.ArrayList;

//多线程按键接口
public class PressNew {
    static boolean isPlaying;

    static class MyTread implements Runnable {
        ArrayList<String> tolLyrics;
        ArrayList<Double> tolTime;

        public MyTread(ArrayList<String> tolLyrics, ArrayList<Double> tolTime) {
            this.tolLyrics = tolLyrics;
            this.tolTime = tolTime;
        }

        @Override
        public void run() {
            for (int i = 0; i < tolLyrics.size() && isPlaying; i++) {
                press(i);
            }
            press('A',0.5);
            press('S',0.5);
            press('D',0.5);
            System.out.println("线程" + Thread.currentThread().getName() + "结束");
            isPlaying = false;
            startFrame.startPlay.setEnabled(true);


            startFrame.textField.setText("弹奏完成");
            System.out.println("弹奏完成");
        }

        void press(char a, double i) {
            Robot key = null;
            try {
                key = new Robot();
            } catch (AWTException e) {
                e.printStackTrace();
            }
            assert key != null;
            key.keyPress(a);
            key.keyRelease(a);
            try {
                Thread.sleep((long) (i * 1000L));
            } catch (InterruptedException e) {
                e.printStackTrace();
            }
        }

        void press(int i) {
            Robot key = null;
            String rollLyrics = tolLyrics.get(i);
            try {
                key = new Robot();
            } catch (AWTException e) {
                e.printStackTrace();
            }
            try {
                assert key != null;
                for (int k = 0; k < rollLyrics.length(); k++) {
                    key.keyPress(rollLyrics.charAt(k));
                    System.out.print(rollLyrics.charAt(k));
                    if (k == 0)
                        startFrame.textField.setText(String.valueOf(rollLyrics.charAt(k)));
                }
                Thread.sleep(20);
                for (int k = 0; k < rollLyrics.length(); k++) {
                    key.keyRelease(rollLyrics.charAt(k));
                }
                System.out.print("\n");
            } catch (NullPointerException | InterruptedException e) {
                System.out.println("多线程按键函数报错：nullPointer");
            }

            //节拍延时
            try {
                if (i < tolTime.size()) {
                    Thread.sleep((long) (tolTime.get(i) * startFrame.t[0] - 20));
                } else {
                    System.out.println("弹奏节拍数目不对;genshin_piano.PressNew:56");
                }
            } catch (InterruptedException e) {
                System.out.println("节拍延时错误");
            }
        }
    }
}
//