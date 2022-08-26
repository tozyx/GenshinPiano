package genshin_piano;

import java.util.ArrayList;

//array格式转换
public class arrayTo {

    //char array to string
    public static String arrayToString (ArrayList<Character> charList){
        String string;
        StringBuilder stringBuilder = new StringBuilder();
        for (char  a : charList){
            stringBuilder.append(a);
        }
        string = stringBuilder.toString();
        return string;
    }

    }