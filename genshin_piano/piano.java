package genshin_piano;

import java.io.File;
import java.util.Properties;

public class piano {
    static File properties;//配置文件
    static Properties pro = new OrderedProperties();

    static final boolean[] isSetPic = {false};//是否是设置开始界面背景
    static final boolean[] isSaved = {true};//编曲界面中编曲目是否保存

    //主函数
    public static void main(String[] args) {

        new startFrame().showStartFrame();

    }
}
