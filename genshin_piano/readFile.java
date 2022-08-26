package genshin_piano;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;

public class readFile {
    public readFile() {
    }

    static void read(ArrayList<String> array, String path, String file) throws IOException {
        BufferedReader br = new BufferedReader(new InputStreamReader(new FileInputStream(path + file), StandardCharsets.UTF_8));

        String line;
        while((line = br.readLine()) != null) {
            array.add(line);
        }
        br.close();
    }

    static String readProperty(String name) {
        try {
            piano.pro.load(new FileInputStream(piano.properties));
        } catch (IOException var2) {
            var2.printStackTrace();
            System.out.println("读取配置文件错误");
        }

        return piano.pro.getProperty(name);
    }
}
