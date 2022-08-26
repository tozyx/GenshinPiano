package genshin_piano;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;

public class writeFile {
    public writeFile() {
    }

    public static void write(ArrayList<Character> comChar, File file) throws IOException {
        BufferedWriter bw = new BufferedWriter((new OutputStreamWriter(new FileOutputStream(file), StandardCharsets.UTF_8)));//utf_8编码
        StringBuilder out = new StringBuilder();
        int count = 2;
        //    'b', '1', '2', '4', '8', 's'
        // 附点'c', 'y', '3', '5', '9', 'd'
        for (int i = 0; i < comChar.size(); i++) {
            if (i == 0) {
                out.append("|1\n");
            }
            if (comChar.get(i) >= 'A' && comChar.get(i) <= 'Z') {
                out.append(comChar.get(i));
            } else {//判断附点
                if (i + 1 < comChar.size() && comChar.get(i + 1) == '.') {//附点编码转换
                    out.append(' ');
                    switch (comChar.get(i)) {
                        case 'b' -> out.append('c').append('\n');
                        case '1' -> out.append('y').append('\n');
                        case '2' -> out.append('3').append('\n');
                        case '4' -> out.append('5').append('\n');
                        case '8' -> out.append('9').append('\n');
                        case 's' -> out.append('d').append('\n');
                        default -> System.out.println("第" + i + "个音节有错，非法编码");
                    }
                } else {//非附点
                    switch (comChar.get(i)) {
                        case 'b', '8', '1', '2', '4', 's' -> out.append(' ').append(comChar.get(i)).append('\n');
                    }
                }
            }
            if (comChar.get(i) == '\n') {
                out.append("\n|").append(count).append("\n");
                count++;
            }
        }

        bw.write(TipFile(out.toString()));
        bw.flush();
        bw.close();
    }

    //文件简谱显示 方便阅读
    public static String TipFile(String s) {
        String[] row = s.split("\n");
        for (int i = 0; i < row.length; i++) {
            if (row[i].length() > 0 && row[i].charAt(0) >= 'A' && row[i].charAt(0) <= 'Z') {
                StringBuilder sb = new StringBuilder();
                sb.append(row[i]).append("\t\t");
                for (int j = 0; j < Coder.yinFuNum(row[i]); j++) {
                    sb.append(abcTo123(row[i].charAt(j)));
                }
                row[i] = sb.toString();
            }
        }
        //字符串数组转字符串 并return
        StringBuilder sb = new StringBuilder();
        for (String value : row) {
            if (value.length() > 0 && value.charAt(0) == '|') {
                sb.append(value);
                sb.append('\n').append('\n');
                continue;
            }
            sb.append(value);
            sb.append('\n');
        }
        return sb.toString();
    }

    //键盘映射音节
    private static String abcTo123(Character a) {
        String out = "";
        switch (a) {
            case 'A' -> out = ("1  ");
            case 'B' -> out = ("5- ");
            case 'C' -> out = ("3- ");
            case 'D' -> out = ("3  ");
            case 'E' -> out = ("3+ ");
            case 'F' -> out = ("4  ");
            case 'G' -> out = ("5  ");
            case 'H' -> out = ("6  ");
            case 'J' -> out = ("7  ");
            case 'M' -> out = ("7- ");
            case 'N' -> out = ("6- ");
            case 'P' -> out = ("\\0 ");
            case 'Q' -> out = ("1+ ");
            case 'R' -> out = ("4+ ");
            case 'S' -> out = ("2  ");
            case 'T' -> out = ("5+ ");
            case 'U' -> out = ("7+ ");
            case 'V' -> out = ("4- ");
            case 'W' -> out = ("2+ ");
            case 'X' -> out = ("2- ");
            case 'Y' -> out = ("6+ ");
            case 'Z' -> out = ("1- ");
        }
        return out;
    }

    static void writeProperty(String name, String value) {
        piano.pro.setProperty(name, value);
        try {
            piano.pro.store(new PrintStream(piano.properties), "encoding:utf-8");
        } catch (IOException var3) {
            var3.printStackTrace();
            System.out.println("配置文件写入错误");
        }

    }
}
