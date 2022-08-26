package genshin_piano;

import java.util.ArrayList;

public class Coder {

    public static void separate(ArrayList<String> readList, ArrayList<String> tolLyrics, ArrayList<Double> tolTime) {
        ArrayList<Character> time = new ArrayList<>();
        tolTime.clear();
        tolLyrics.clear();
        for (String s : readList) {
            if (s.length() > 0) {
                char first = s.charAt(0);
                if (first >= 'A' && first <= 'Z') {
                    tolLyrics.add(s.substring(0, yinFuNum(s)));
                    time.add(numList(s));
                }
            }
        }
        tolTime.addAll(toPressTime(time));
    }


    //分离显示字符串
    public static String readListToCom(ArrayList<String> readList) {
        StringBuilder sb = new StringBuilder();
        for (String i : readList) {
            if (i.length() != 0) {
                if (i.charAt(0) == '|' && !i.equals("|1")) {
                    sb.append("\n");
                } else if (i.charAt(0) >= 'A' && i.charAt(0) <= 'Z') {
                    sb.append(" ").append(i, 0, yinFuNum(i));
                    //    'b', '1', '2', '4', '8', 's'
                    // 附点'c', 'y', '3', '5', '9', 'd'
                    switch (numList(i)) {
                        case 'c' -> sb.append("b.");
                        case 'y' -> sb.append("1.");
                        case '3' -> sb.append("2.");
                        case '5' -> sb.append("4.");
                        case '9' -> sb.append("8.");
                        case 'd' -> sb.append("s.");
                        default -> sb.append(numList(i));
                    }
                }
            }
        }
        return sb.toString();
    }

    //判断几个同时弹奏音符
    public static int yinFuNum(String readList) {
        int i = 0;
        for (; i < readList.length(); i++) {
            if (readList.charAt(i) == ' ') {
                break;
            }
        }
        return i;
    }

    //返回节拍字符   'b', '1', '2', '4', '8', 's' 附点'c', 'y', '3', '5', '9', 'd'
    private static char numList(String reaList) {
        for (int i = 0; i < reaList.length(); i++) {
            if (reaList.charAt(i) == ' ') {
                return (reaList.charAt(i + 1));
            }
        }
        System.out.println("时间字符分离错误：Coder.numList");
        return 0;
    }

    //转换弹奏真实时间
    private static ArrayList<Double> toPressTime(ArrayList<Character> oneL) {
        ArrayList<Double> pressChar = new ArrayList<>();
        for (Character character : oneL) {
            //    'b', '1', '2', '4', '8', 's'
            // 附点'c', 'y', '3', '5', '9', 'd'
            switch (character) {
                case 'b' -> pressChar.add(0.5);
                case '1', '2', '4', '8' -> pressChar.add(Double.valueOf(character) - '0');
                case 's', '9' -> pressChar.add(12.0);
                case 'c' -> pressChar.add(0.5 * 3 / 2);
                case 'y' -> pressChar.add(1.5);
                case '3' -> pressChar.add(3.0);
                case '5' -> pressChar.add(6.0);
                case 'd' -> pressChar.add(18.0);
            }
        }
        return pressChar;
    }


}
