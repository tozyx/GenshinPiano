package genshin_piano;

import java.awt.*;
import java.awt.datatransfer.Clipboard;
import java.awt.datatransfer.StringSelection;
import java.awt.datatransfer.Transferable;
import java.awt.event.*;
import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Objects;
import javax.swing.*;
import javax.swing.event.DocumentEvent;
import javax.swing.event.DocumentListener;
import javax.swing.text.DefaultCaret;

import static java.awt.Toolkit.getDefaultToolkit;


public class componentFrame {
    ArrayList<JButton> jieButtonsList = new ArrayList<>();
    ArrayList<JButton> shuButtonsList = new ArrayList<>();
    ArrayList<JButton> wholeButtonsList = new ArrayList<>();
    ArrayList<String> readList = new ArrayList<>();
    ArrayList<JTextField> rgb = new ArrayList<>();
    //按钮颜色
    final int[] jButtonRGB = new int[3];
    final boolean[] hadChar = {false};
    final boolean[] isSaved = piano.isSaved;
    final boolean[] fuDian = {false};//判断是否为附点
    final boolean[] isBackToStart = new boolean[1];//判断是否返回开始界面
    final boolean[] isEmpty = new boolean[1];//判断是否清空
    final boolean[] copy = {false};//保存路径是否复制到剪切板
    final boolean[] isOpaque = new boolean[1];//界面透明
    final boolean[] jbOpaque = new boolean[1];//按钮透明
    final String[] WhatRGB = {"Font"};//rgb设置选择
    JPanel wholeCom = new backgroundPanel();
    Frame component = new Frame("编曲");
    MenuItem top1 = new MenuItem();
    //个性化菜单使能

    Point locationOnScreen;//面板屏幕位置 调用 .x .y
    Dimension componentSize;//面板大小 调用 .height
    FileDialog imageOpening = new FileDialog(component, "选择背景图片路径", FileDialog.LOAD);
    JDialog checkTimeFrame = new JDialog(component, "输入每小节节拍数", true);
    JDialog setJColor = new JDialog(component, "设置颜色(RGB)(0-255)", true);//设置按钮颜色面板
    JTextField pai = new JTextField();
    JTextField red = new JTextField();
    JTextField green = new JTextField();
    JTextField blue = new JTextField();
    JButton yesPai = new JButton("开始检查");
    JButton noPai = new JButton("取消检查");
    JButton Pai = new JButton("拍");
    JTextField previewColor = new JTextField("");
    JButton setColor = new JButton("应用");
    JButton cancelColor = new JButton("不更改");
    FileDialog opening = new FileDialog(component, "选择文件路径", FileDialog.LOAD);
    FileDialog saving = new FileDialog(component, "保存文件", FileDialog.SAVE);
    JTextArea com = new JTextArea("谱子：");
    JScrollPane scrollPane = new JScrollPane(com);
    JTextField tip = new JTextField("提示");
    StringBuilder tips = new StringBuilder();
    Toolkit kit = Toolkit.getDefaultToolkit();
    Dimension windowSize = kit.getScreenSize();

    startFrame startFrame = new startFrame();
    //文本显示panel
    JPanel text = new JPanel(new GridLayout(2, 1, 10, 0));
    //按钮panel 5*7
    JPanel button = new JPanel(new GridLayout(5, 7, 4, 2));
    //总panel

    JButton A0 = new JButton("1/8 C");
    JButton empty = new JButton("");//填充排版按钮
    JButton empty1 = new JButton("");
    JButton empty2 = new JButton("");
    JButton empty3 = new JButton("");
    JButton empty4 = new JButton("");
    JButton empty5 = new JButton("");
    JButton P = new JButton("0 ");
    JButton A1 = new JButton("1/4 C");
    JButton A2 = new JButton("1/2 C");
    JButton A4 = new JButton("C");
    JButton A8 = new JButton("C -");
    JButton A12 = new JButton("C - - ");
    JButton Ad = new JButton("C .");
    JButton A = new JButton("1 ");
    JButton S = new JButton("2 ");
    JButton D = new JButton("3 ");
    JButton F = new JButton("4 ");
    JButton G = new JButton("5 ");
    JButton H = new JButton("6 ");
    JButton J = new JButton("7 ");
    JButton Q = new JButton("1+");
    JButton W = new JButton("2+");
    JButton E = new JButton("3+");
    JButton R = new JButton("4+");
    JButton T = new JButton("5+");
    JButton Y = new JButton("6+");
    JButton U = new JButton("7+");
    JButton Z = new JButton("1-");
    JButton X = new JButton("2-");
    JButton C = new JButton("3-");
    JButton V = new JButton("4-");
    JButton B = new JButton("5-");
    JButton N = new JButton("6-");
    JButton M = new JButton("7-");
    JButton yes = new JButton("是");
    JButton no = new JButton("否");
    JDialog componentExitLog = new JDialog(component, "检测到未保存编曲是否保存", true);

    //个性化按钮
    void setJb(JButton a, int red, int green, int blue) {
        a.setBackground(new Color(red, green, blue));
    }

    //编曲界面初始化
    void componentInitialize() {
        //设置图标
        Image icon = getDefaultToolkit().getImage(getClass().getClassLoader().getResource("resource/co.png"));
        component.setIconImage(icon);
        //初始化
        wholeCom.setLayout(new GridLayout(2, 1, 0, 6));
        com.setText("谱子：");
        tip.setText("提示");
        setInitialize();//初始个性化


        checkTimeFrame.setLayout(new GridLayout(2, 2, 2, 0));
        com.setFont(new Font("黑体", Font.PLAIN, 18));
        tip.setEditable(false);
        tip.setFocusable(false);
        tip.setFont(new Font("黑体", Font.ITALIC, 15));
        componentExitLog.setLayout(new GridLayout(1, 2, 0, 0));
        componentExitLog.add(yes);
        componentExitLog.add(no);


        //文本整合排序
        text.add(scrollPane);
        text.add(tip);
        com.setBackground(tip.getBackground());
        com.setLineWrap(true);//自动换行 不含\n
        scrollPane.setOpaque(false);
        scrollPane.setBorder(BorderFactory.createEmptyBorder());
        scrollPane.getVerticalScrollBar().setBorder(BorderFactory.createEmptyBorder());
        scrollPane.getVerticalScrollBar().setOpaque(false);


        //文本域置底
        DefaultCaret caret = (DefaultCaret) com.getCaret();
        caret.setUpdatePolicy(DefaultCaret.ALWAYS_UPDATE);


        //添加按钮和文本框到集合
        jieButtonsList.clear();
        shuButtonsList.clear();
        wholeButtonsList.clear();
        rgb.clear();
        rgb.add(red);
        rgb.add(green);
        rgb.add(blue);
        //添加button到集合中
        jieButtonsList.addAll(Arrays.asList(A0, A1, A2, A4, A8, A12));
        shuButtonsList.addAll(Arrays.asList(Q, W, E, R, T, Y, U, A, S, D, F, G, H, J, Z, X, C, V, B, N, M, P));
        wholeButtonsList.addAll(jieButtonsList);
        wholeButtonsList.addAll(shuButtonsList);
        wholeButtonsList.add(Ad);

        //添加按钮组件到button panel中 排版
        ArrayList<JButton> emptyButton = new ArrayList<>(Arrays.asList(empty, empty1, empty2, empty3, empty4, empty5));
        for (JButton j : emptyButton) {
            button.add(j);
            j.setEnabled(false);
            j.setBorder(BorderFactory.createEmptyBorder());//无边框
            j.setContentAreaFilled(false);//透明
        }
        button.add(P);
        P.setToolTipText("休止音符");

        setColor.setBorder(BorderFactory.createEmptyBorder());
        cancelColor.setBorder(BorderFactory.createEmptyBorder());
        previewColor.setBorder(BorderFactory.createEmptyBorder());
        setColor.setContentAreaFilled(false);
        cancelColor.setContentAreaFilled(false);
        previewColor.setEditable(false);
        previewColor.setFocusable(false);
        for (JButton jieButton : jieButtonsList) {
            button.add(jieButton);
        }
        A0.setToolTipText("八分之一拍");
        A1.setToolTipText("四分之一拍");
        A2.setToolTipText("二分之一拍");
        A4.setToolTipText("全拍");
        A8.setToolTipText("两拍");
        A12.setToolTipText("三拍");
        button.add(Ad);
        Ad.setToolTipText("附点");
        for (JButton shuButton : shuButtonsList) {
            if (shuButton != P) {
                button.add(shuButton);
            }
        }
        Pai.setEnabled(false);
        Pai.setFocusable(false);
        Pai.setBorder(BorderFactory.createEmptyBorder());
        yesPai.setBorder(BorderFactory.createEmptyBorder());
        yesPai.setContentAreaFilled(false);
        noPai.setBorder(BorderFactory.createEmptyBorder());
        noPai.setContentAreaFilled(false);

        //按钮外观初始化
        for (JButton wholeButton : wholeButtonsList) {
            setJb(wholeButton, jButtonRGB[0], jButtonRGB[1], jButtonRGB[2]);
            wholeButton.setForeground(new Color(Integer.parseInt(readFile.readProperty("FontRed")), Integer.parseInt(readFile.readProperty("FontGreen")), Integer.parseInt(readFile.readProperty("FontBlue"))));
            wholeButton.setBorder(BorderFactory.createEmptyBorder());
            wholeButton.setOpaque(jbOpaque[0]);
        }
        //字体颜色
        com.setForeground(new Color(Integer.parseInt(readFile.readProperty("FontRed")), Integer.parseInt(readFile.readProperty("FontGreen")), Integer.parseInt(readFile.readProperty("FontBlue"))));
        tip.setForeground(new Color(Integer.parseInt(readFile.readProperty("FontRed")), Integer.parseInt(readFile.readProperty("FontGreen")), Integer.parseInt(readFile.readProperty("FontBlue"))));


        setOpaque();
        wholeCom.add(text);
        wholeCom.add(button);
        component.add(wholeCom);

        checkTimeFrame.add(pai);
        checkTimeFrame.add(Pai);
        checkTimeFrame.add(yesPai);
        checkTimeFrame.add(noPai);


        //文本框无边框
        pai.setOpaque(false);
        pai.setBorder(BorderFactory.createEmptyBorder());
        text.setBorder(BorderFactory.createEmptyBorder());
        com.setBorder(BorderFactory.createEmptyBorder());
        tip.setBorder(BorderFactory.createEmptyBorder());
        red.setBorder(BorderFactory.createEmptyBorder());
        green.setBorder(BorderFactory.createEmptyBorder());
        blue.setBorder(BorderFactory.createEmptyBorder());
        red.setBackground(new Color(230, 180, 180));
        green.setBackground(new Color(180, 230, 180));
        blue.setBackground(new Color(180, 180, 230));
        red.setBorder(BorderFactory.createEmptyBorder());
        green.setBorder(BorderFactory.createEmptyBorder());
        blue.setBorder(BorderFactory.createEmptyBorder());

        frameListenerInit();

        componentMenuInitialize();

        component.setVisible(true);
    }

    private void componentMenuInitialize() {

        //菜单条
        MenuBar bar = new MenuBar();
        //菜单
        Menu menu = new Menu("菜单");
        Menu menuFile = new Menu("文件");
        Menu edit = new Menu("编辑");
        Menu set = new Menu("个性化");
        Menu background = new Menu("背景");
        Menu setJbColor = new Menu("按钮颜色");
        Menu setFontColor = new Menu("字体颜色");
        //菜单项
        MenuItem Image = new MenuItem("本地图片");
        MenuItem defaultSet = new MenuItem("恢复默认");
        MenuItem colorItem = new MenuItem("按钮RGB...");
        MenuItem jbOpaque = new MenuItem("");
        MenuItem JbDefault = new MenuItem("恢复默认");
        MenuItem FontColor = new MenuItem("字体RGB...");
        MenuItem FontDefault = new MenuItem("恢复默认");
        MenuItem startFrame = new MenuItem("开始界面");
        MenuItem open = new MenuItem("打开文件...");
        MenuItem save = new MenuItem("另存为", new MenuShortcut(83, false));
        MenuItem copyItem = new MenuItem("开启/关闭复制保存路径到剪切板");
        MenuItem back = new MenuItem("退格", new MenuShortcut(90, false));
        MenuItem skip = new MenuItem("在此分节(可用于检查)", new MenuShortcut(KeyEvent.VK_X, false));
        MenuItem empty = new MenuItem("清空");
        MenuItem check = new MenuItem("检查节拍(先打开文件)");
        final String[] hidestring = new String[]{"隐藏"};
        final MenuItem hide = new MenuItem(hidestring[0] + "提示栏");


        MenuItem isOpaque = new MenuItem("");
        if (Objects.equals(readFile.readProperty("isOpaque"), "true")) {
            isOpaque.setLabel("△透明");
        } else {
            isOpaque.setLabel("▲透明");
        }
        if (Objects.equals(readFile.readProperty("jbOpaque"), "true")) {
            jbOpaque.setLabel("△透明");
        } else {
            jbOpaque.setLabel("▲透明");
        }
        setJColor.setLayout(new GridLayout(2, 3, 1, 1));
        setJColor.add(red);
        setJColor.add(green);
        setJColor.add(blue);
        setJColor.add(setColor);
        setJColor.add(cancelColor);
        setJColor.add(previewColor);
        check.setEnabled(false);

        //组装菜单栏
        //文件菜单
        menuFile.add(open);
        menuFile.add(save);
        menuFile.add(copyItem);
        //编辑菜单
        edit.add(back);
        edit.add(skip);
        edit.add(check);
        edit.add(empty);
        //个性化菜单
        set.add(background);
        setJbColor.add(colorItem);
        setJbColor.add(jbOpaque);
        setJbColor.add(JbDefault);
        set.add(setJbColor);
        setFontColor.add(FontColor);
        setFontColor.add(FontDefault);
        set.add(setFontColor);
        set.add(hide);
        //组装
        background.add(isOpaque);
        background.add(Image);
        background.add(defaultSet);


        //菜单菜单
        menu.add(startFrame);
        menu.add(new MenuItem("-"));
        menu.add(this.top1);
        //菜单bar
        bar.add(menu);
        bar.add(menuFile);
        bar.add(edit);
        bar.add(set);
        component.setMenuBar(bar);

        if ( genshin_piano.startFrame.isTop[0]) {
            component.setAlwaysOnTop(true);
            top1.setLabel("窗口取消置顶");
        } else {
            top1.setLabel("窗口置顶");
            component.setAlwaysOnTop(false);
        }
        //编曲菜单监听器
        {
            noPai.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    checkTimeFrame.setVisible(false);
                }
            });
            yesPai.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    check();
                }
            });
            check.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    pai.setText("");
                    dialogCenter(checkTimeFrame, 200, 82);
                }
            });
            jbOpaque.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    if (Objects.equals(readFile.readProperty("jbOpaque"), "true")) {
                        writeFile.writeProperty("jbOpaque", "false");
                    } else {
                        writeFile.writeProperty("jbOpaque", "true");
                    }
                    refresh();
                }
            });
            FontDefault.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    writeFile.writeProperty("FontRed", "0");
                    writeFile.writeProperty("FontGreen", "0");
                    writeFile.writeProperty("FontBlue", "0");
                    refresh();
                }
            });

            JbDefault.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    writeFile.writeProperty("jButtonRed", "240");
                    writeFile.writeProperty("jButtonGreen", "200");
                    writeFile.writeProperty("jButtonBlue", "200");
                    writeFile.writeProperty("jbOpaque", "true");
                    refresh();
                }
            });
            setColor.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    if (isRGBNumber()) {
                        if (WhatRGB[0].equals("Button")) {
                            writeFile.writeProperty("jButtonRed", red.getText());
                            writeFile.writeProperty("jButtonGreen", green.getText());
                            writeFile.writeProperty("jButtonBlue", blue.getText());
                        } else if (WhatRGB[0].equals("Font")) {
                            writeFile.writeProperty("FontRed", red.getText());
                            writeFile.writeProperty("FontGreen", green.getText());
                            writeFile.writeProperty("FontBlue", blue.getText());
                        }
                        setJColor.setVisible(false);
                        refresh();
                    }
                }
            });

            cancelColor.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    setJColor.setVisible(false);

                }
            });

            FontColor.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    if (readFile.readProperty("FontRed") != null && readFile.readProperty("FontGreen") != null && readFile.readProperty("FontBlue") != null) {
                        red.setText(readFile.readProperty("FontRed"));
                        green.setText(readFile.readProperty("FontGreen"));
                        blue.setText(readFile.readProperty("FontBlue"));
                    } else {
                        writeFile.writeProperty("FontRed", "0");
                        writeFile.writeProperty("FontGreen", "0");
                        writeFile.writeProperty("FontBlue", "0");
                    }
                    previewColor();
                    WhatRGB[0] = "Font";
                    setJColor.setName("设置字体颜色(RGB)(0-255)");
                    dialogCenter(setJColor, 300, 80);
                }
            });

            colorItem.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
//初始化按钮颜色
                    if (readFile.readProperty("jButtonRed") != null && readFile.readProperty("jButtonGreen") != null && readFile.readProperty("jButtonBlue") != null) {
                        red.setText(readFile.readProperty("jButtonRed"));
                        green.setText(readFile.readProperty("jButtonGreen"));
                        blue.setText(readFile.readProperty("jButtonBlue"));
                    } else {
                        writeFile.writeProperty("jButtonRed", "240");
                        writeFile.writeProperty("jButtonGreen", "200");
                        writeFile.writeProperty("jButtonBlue", "200");
                    }
                    previewColor();
                    WhatRGB[0] = "Button";
                    setJColor.setName("设置按钮颜色(RGB)(0-255)");
                    dialogCenter(setJColor, 300, 80);
                }
            });
            //背景初始化
            defaultSet.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    writeFile.writeProperty("isOpaque", "true");
                    writeFile.writeProperty("componentImagePath", "");
                    refresh();
                }
            });
            Image.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    getImagePath();
                    writeFile.writeProperty("isOpaque", "false");
                    refresh();
                }
            });
            isOpaque.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    if (Objects.equals(readFile.readProperty("isOpaque"), "true")) {
                        writeFile.writeProperty("isOpaque", "false");
                    } else {
                        writeFile.writeProperty("isOpaque", "true");
                    }
                    refresh();
                }
            });
            top1.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    genshin_piano.startFrame.isTop[0] = !genshin_piano.startFrame.isTop[0];
                    if (genshin_piano.startFrame.isTop[0]) {
                        component.setAlwaysOnTop(true);
                        top1.setLabel("窗口取消置顶");
                        tip.setText("窗口已置顶");
                    } else {
                        top1.setLabel("窗口置顶");
                        tip.setText("窗口已取消置顶");
                        component.setAlwaysOnTop(false);
                    }

                }
            });
            open.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    if (com.getText().length() == 0 || Objects.equals(com.getText(), "谱子：")) {
                        readList.clear();
                        top1.setLabel("窗口置顶");
                        tip.setText("窗口已取消置顶");
                        component.setAlwaysOnTop(false);
                        open();
                        if (readList.size() != 0) {
                            String s = Coder.readListToCom(readList);
//                            for (String a:readList){
//                                System.out.println(a);
//                            }
                            showTip();
                            com.setText(s);
                            check.setEnabled(true);
                            tip.setText("打开成功");

                        } else {
                            tip.setText("未读取到文件");
                        }
                    } else {
                        tip.setText("请清空后重试");
                    }
                }
            });
            empty.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    if (isSaved[0]) {
                        com.setText("");
                        tip.setText("已清空");
                        tips.delete(0, tips.length());
                    } else {
                        isBackToStart[0] = false;
                        isEmpty[0] = true;
                        dialogCenter(componentExitLog, 250, 60);
                    }

                }
            });
            skip.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    if (!hadChar[0] && !Objects.equals(com.getText(), "谱子：") &&
                            !(Objects.equals(tip.getText(), "分节成功") || Objects.equals(tip.getText(), "插入失败 请输入完整音节")
                                    || (tip.getText().equals("退格")))) {
                        com.append("\n");
                        Ad.setEnabled(false);
                        tip.setText("分节成功");
                    } else {
                        tip.setText("插入失败 请输入完整音节");
                    }

                }
            });
            copyItem.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    copy[0] = !copy[0];
                    tip.setText("功能：复制保存路径到剪切板：" + copy[0]);
                }
            });
            back.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    ArrayList<Character> comChar = comToArray();
                    back(comChar);
                    com.setText(arrayTo.arrayToString(comChar));
                }
            });
            //到启动面板
            startFrame.addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    if (isSaved[0]) {
                        component.dispose();
                        new startFrame().showStartFrame();
                    } else {
                        isBackToStart[0] = true;
                        dialogCenter(componentExitLog, 250, 60);
                    }
                }
            });
            //隐藏提示栏
            hide.addActionListener(new

                                           AbstractAction() {
                                               @Override
                                               public void actionPerformed(ActionEvent e) {
                                                   if (hidestring[0].equals("隐藏")) {
                                                       hidestring[0] = "显示";
                                                       tip.setVisible(false);

                                                   } else {
                                                       hidestring[0] = "隐藏";
                                                       tip.setVisible(true);
                                                   }
                                                   hide.setLabel(hidestring[0] + "提示栏");
                                               }
                                           });
            //保存文件
            save.addActionListener(new AbstractAction() {
                public void actionPerformed(ActionEvent e) {
                    save();
                }
            });
        }
    }

    //总监听
    private void frameListenerInit() {
        //内容更新监听
        {
            //面板更新监听
            {
                //关闭
                component.addWindowListener(new WindowAdapter() {
                    @Override
                    public void windowClosing(WindowEvent e) {
                        if (isSaved[0]) {
                            System.exit(0);
                        } else {
                            dialogCenter(componentExitLog, 250, 60);
                        }
                    }
                });
                //写入配置文件面板大小信息
                component.addWindowListener(new WindowAdapter() {
                    @Override
                    public void windowLostFocus(WindowEvent e) {
                        writeFile.writeProperty("componentWide", String.valueOf(component.getWidth()));
                        writeFile.writeProperty("componentHeight", String.valueOf(component.getHeight()));
                    }
                });
                component.addComponentListener(new ComponentAdapter() {
                    @Override
                    public void componentHidden(ComponentEvent e) {
                        //记录编曲界面大小
                        writeFile.writeProperty("componentWide", String.valueOf(component.getWidth()));
                        writeFile.writeProperty("componentHeight", String.valueOf(component.getHeight()));
                    }

                    @Override
                    public void componentResized(ComponentEvent e) {
                        writeFile.writeProperty("componentWide", String.valueOf(component.getWidth()));
                        writeFile.writeProperty("componentHeight", String.valueOf(component.getHeight()));
                    }
                });
                componentExitLog.addWindowListener(new WindowAdapter() {
                    @Override
                    public void windowClosing(WindowEvent e) {
                        isBackToStart[0] = false;
                    }
                });
            }

            //滚轮设置rgb
            {
                for (JTextField Rgb : rgb) {
                    Rgb.addMouseWheelListener(new MouseAdapter() {
                        @Override
                        public void mouseWheelMoved(MouseWheelEvent e) {
                            if (!isNumeric(Rgb.getText()))
                                Rgb.setText("0");
                            if (Integer.parseInt(Rgb.getText().trim()) + 2 <= 255 && e.getWheelRotation() == -1)
                                Rgb.setText(String.valueOf(Integer.parseInt(Rgb.getText().trim()) + 2));
                            else if (Integer.parseInt(Rgb.getText().trim()) - 2 >= 0 && e.getWheelRotation() == 1)
                                Rgb.setText(String.valueOf(Integer.parseInt(Rgb.getText().trim()) - 2));
                        }
                    });
                    Rgb.getDocument().addDocumentListener(new DocumentListener() {
                        @Override
                        public void insertUpdate(DocumentEvent e) {
                            previewColor();
                            previewColor.setText("");
                        }

                        @Override
                        public void removeUpdate(DocumentEvent e) {
                            previewColor();
                            previewColor.setText("");
                        }

                        @Override
                        public void changedUpdate(DocumentEvent e) {
                            previewColor();
                            previewColor.setText("");
                        }
                    });
                }
            }

            //操作更新监听
            tip.getDocument().addDocumentListener(new DocumentListener() {
                @Override
                public void insertUpdate(DocumentEvent e) {
                    if (com.getText().length() != 0) {
                        isSaved[0] = false;
                    }
                }

                @Override
                public void removeUpdate(DocumentEvent e) {
                }

                @Override
                public void changedUpdate(DocumentEvent e) {
                }
            });
        }

        //按键监听

        Ad.setEnabled(false);
        yes.setContentAreaFilled(false);
        yes.setFont(new Font("黑体", Font.PLAIN, 13));
        yes.setFocusable(false);
        yes.setBorder(BorderFactory.createEmptyBorder());
        yes.addActionListener(new AbstractAction() {
            @Override
            public void actionPerformed(ActionEvent e) {
                save();
                if (isSaved[0] && !isEmpty[0]) {
                    System.exit(0);
                } else {
                    componentExitLog.setVisible(false);
                }
            }
        });
        no.setContentAreaFilled(false);
        no.setFont(new Font("黑体", Font.PLAIN, 13));
        no.setFocusable(false);
        no.setBorder(BorderFactory.createEmptyBorder());
        no.addActionListener(new AbstractAction() {
            public void actionPerformed(ActionEvent e) {
                componentExitLog.setVisible(false);
                if (isBackToStart[0]) {
                    component.dispose();
                    startFrame.showStartFrame();
                } else if (!isEmpty[0]) {
                    System.exit(0);
                } else {
                    com.setText("");
                    tip.setText("已清空");
                    isEmpty[0] = false;
                }
                isBackToStart[0] = false;
                isSaved[0] = true;
            }
        });

        //音节按键监听
        final char[] jieC = {'b', '1', '2', '4', '8', 's'};
        final String[] jieS = {"\t八分之一拍", "\t四分之一拍", "\t二分之一拍", "\t一拍", "\t两拍", "\t三拍"};
        for (int i = 0; i < this.jieButtonsList.size(); ++i) {
            int finalI = i;
            jieButtonsList.get(i).addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    if (hadChar[0]) {
                        Ad.setEnabled(true);
                        fuDian[0] = true;
                        com.append(String.valueOf(jieC[finalI]));
                        tips.append(jieS[finalI]);
                        showTip();
                        hadChar[0] = false;
                    }
                }
            });
        }
        //音调按键监听
        final char[] shuC = new char[]{'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'A', 'S', 'D', 'F', 'G', 'H', 'J', 'Z', 'X', 'C', 'V', 'B', 'N', 'M', 'P'};
        final String[] shuS = new String[]{"1+", "2+", "3+", "4+", "5+", "6+", "7+", "1", "2", "3", "4", "5", "6", "7", "1-", "2-", "3-", "4-", "5-", "6-", "7-", "0"};

        for (int i = 0; i < this.shuButtonsList.size(); ++i) {
            int finalI = i;
            shuButtonsList.get(i).addActionListener(new AbstractAction() {
                @Override
                public void actionPerformed(ActionEvent e) {
                    if (com.getText().equals("谱子：")) {
                        com.setText("");
                    }
                        Ad.setEnabled(false);
                        if (!hadChar[0]) {
                            com.append(" ");
                        }
                        com.append(String.valueOf(shuC[finalI]));
                        fuDian[0] = false;
                        hadChar[0] = true;
                        tips.delete(0, tips.length());
                        tips.append(shuS[finalI]);
                        showTip();
                }
            });
        }
//附点注意
//
        Ad.addActionListener(new AbstractAction() {
            @Override
            public void actionPerformed(ActionEvent e) {
                if (fuDian[0] && !hadChar[0]) {
                    Ad.setEnabled(false);
                    com.append(".");
                    tips.append("附点");
                    showTip();
                }
                fuDian[0] = false;
            }
        });
    }

    //各种个性化初始化
    private void setInitialize() {

        {
            //设置透明panel和按钮
            if (readFile.readProperty("isOpaque") == null) {
                writeFile.writeProperty("isOpaque", "true");
                isOpaque[0] = true;
            } else isOpaque[0] = Objects.equals(readFile.readProperty("isOpaque"), "true");

            if (readFile.readProperty("jbOpaque") == null) {
                writeFile.writeProperty("jbOpaque", "true");
                jbOpaque[0] = true;
            } else jbOpaque[0] = Objects.equals(readFile.readProperty("jbOpaque"), "true");

            //初始化编曲界面大小
            if (readFile.readProperty("componentWide") == null) {
                component.setBounds((int) windowSize.getWidth() / 2 - 650 / 2, (int) windowSize.getHeight() / 2 - 250 / 2, 650, 250);
            } else {
                component.setBounds((int) windowSize.getWidth() / 2 - Integer.parseInt(readFile.readProperty("componentWide")) / 2, (int) windowSize.getHeight() / 2 - Integer.parseInt(readFile.readProperty("componentHeight")) / 2, Integer.parseInt(readFile.readProperty("componentWide")), Integer.parseInt(readFile.readProperty("componentHeight")));
            }
            if (readFile.readProperty("jButtonRed") == null || readFile.readProperty("jButtonGreen") == null || readFile.readProperty("jButtonBlue") == null) {
                writeFile.writeProperty("jButtonRed", "240");
                writeFile.writeProperty("jButtonGreen", "200");
                writeFile.writeProperty("jButtonBlue", "200");
            }
            if (readFile.readProperty("FontRed") == null || readFile.readProperty("FontGreen") == null || readFile.readProperty("FontBlue") == null) {
                writeFile.writeProperty("FontRed", "0");
                writeFile.writeProperty("FontGreen", "0");
                writeFile.writeProperty("FontBlue", "0");
            }

            jButtonRGB[0] = Integer.parseInt(readFile.readProperty("jButtonRed"));
            jButtonRGB[1] = Integer.parseInt(readFile.readProperty("jButtonGreen"));
            jButtonRGB[2] = Integer.parseInt(readFile.readProperty("jButtonBlue"));
        }

    }

    //退格调用函数
    private void back(ArrayList<Character> comChar) {
        if (comChar.size() - 1 >= 0) {
            if (comChar.get(comChar.size() - 1) > 'A' && comChar.get(comChar.size() - 1) < 'Z') {
                if (comChar.get(comChar.size() - 2) == ' ') {
                    comChar.remove(comChar.size() - 1);
                    comChar.remove(comChar.size() - 1);
                } else {
                    comChar.remove(comChar.size() - 1);
                }
            } else if (comChar.get(comChar.size() - 1) == '：') {
                comChar.clear();
            } else if (comChar.get(comChar.size() - 1) != '.') {
                comChar.remove(comChar.size() - 1);
            } else {
                comChar.remove(comChar.size() - 1);
                comChar.remove(comChar.size() - 1);
            }
            tip.setText("退格");
            hadChar[0] = (comChar.size() - 1 >= 0 && comChar.get(comChar.size() - 1) > 'A' && comChar.get(comChar.size() - 1) < 'Z');
        }
    }

    //编曲转char集合
    public ArrayList<Character> comToArray() {
        ArrayList<Character> arrayList = new ArrayList<>();
        for (int i = 0; i < com.getText().length(); i++) {
            arrayList.add(com.getText().charAt(i));
        }
        return arrayList;
    }

    //预览颜色显示
    private void previewColor() {
        if (isNumeric(red.getText()) && isNumeric(green.getText()) && isNumeric(blue.getText())) {
            if (Integer.parseInt(red.getText()) >= 0 && Integer.parseInt(red.getText()) <= 255 && Integer.parseInt(green.getText()) >= 0 &&
                    Integer.parseInt(green.getText()) <= 255 && Integer.parseInt(blue.getText()) >= 0 && Integer.parseInt(blue.getText()) <= 255)
                previewColor.setBackground(new Color(Integer.parseInt(red.getText()), Integer.parseInt(green.getText()), Integer.parseInt(blue.getText())));
        }

    }

    //判断字符串是否为数字 不含.
    boolean isNumeric(String str) {
        if (str == null || str.equals("")) {
            return false;
        }
        int sz = str.length();
        for (int i = 0; i < sz; i++) {
            if (!Character.isDigit(str.charAt(i))) {
                return false;
            }
        }
        return true;
    }

    //打开文件查错
    private void check() {
        if (isNumeric(pai.getText())) {

        }
    }

    //判断rgb输入是否合法并写入配置文件
    private boolean isRGBNumber() {
        boolean i;
        if (isNumeric(red.getText()) && isNumeric(green.getText()) && isNumeric(blue.getText())) {
            i = Integer.parseInt(red.getText().trim()) >= 0 && Integer.parseInt(red.getText().trim()) <= 256 &&
                    Integer.parseInt(green.getText().trim()) >= 0 && Integer.parseInt(green.getText().trim()) <= 256 &&
                    Integer.parseInt(blue.getText().trim()) >= 0 && Integer.parseInt(blue.getText().trim()) <= 256;
        } else {
            i = false;
        }
        if (i) {
            jButtonRGB[0] = Integer.parseInt(red.getText().trim());
            jButtonRGB[1] = Integer.parseInt(green.getText().trim());
            jButtonRGB[2] = Integer.parseInt(blue.getText().trim());
        }

        return i;
    }

    //各组件透明
    private void setOpaque() {
        //透明
        button.setOpaque(isOpaque[0]);
        wholeCom.setOpaque(isOpaque[0]);
        text.setOpaque(isOpaque[0]);
        scrollPane.getViewport().setOpaque(isOpaque[0]);
        com.setOpaque(isOpaque[0]);
        tip.setOpaque(isOpaque[0]);
    }

    private void getImagePath() {
        String imagePath;
        imageOpening.setFile("*.jpg;*.png;*.gif");
        imageOpening.setVisible(true);
        component.setAlwaysOnTop(false);
        top1.setLabel("置顶窗口");
        imageOpening.setAlwaysOnTop(true);
        if (imageOpening.getFile() != null) {
            imagePath = imageOpening.getDirectory() + imageOpening.getFile();
            writeFile.writeProperty("componentImagePath", imagePath);
            System.out.println("图片路径:" + imagePath);
        } else {
            System.out.println("读取图片失败 请选择图片");
        }

    }

    //显示面板文字
    void showTip() {
        tip.setText(tips.toString());
    }

    //打开文件方法
    private void open() {
        String[] opendi = new String[1];
        opendi[0] = null;
        String[] openfile = new String[1];
        openfile[0] = null;
        opening.setFile("*.GenshinPiano");
        opening.setVisible(true);
        opening.setAlwaysOnTop(true);
        if (opening.getFile() != null) {
            opendi[0] = opening.getDirectory();
            openfile[0] = opening.getFile();
            System.out.println("打开文件路径：" + opendi[0] + openfile[0]);
            try {
                readFile.read(readList, opendi[0], openfile[0]);
                tip.setText("打开文件成功");
                opening.setFile(null);
                opening.setVisible(false);
                System.out.println("打开文件成功");
            } catch (IOException e) {
                e.printStackTrace();
            }
        } else {
            tip.setText("文件打开失败");
        }

    }

    //保存方法
    private void save() {
        saving.setFile("*.GenshinPiano");
        tip.setText("窗口已取消置顶");
        component.setAlwaysOnTop(false);
        saving.setAlwaysOnTop(true);
        saving.setVisible(true);
        componentExitLog.setVisible(false);
        if (saving.getFile() != null) {
            tip.setText("保存成功");
            isSaved[0] = true;
            File file = new File(saving.getDirectory() + saving.getFile());
            System.out.println("保存路径:" + file);
            ArrayList<Character> comWholeChar = comToArray();
            try {
                writeFile.write(comWholeChar, file);
            } catch (IOException e) {
                e.printStackTrace();
                System.out.println("文件写入函数错误 componentFrame.save:1027");
            }
            if (copy[0]) {
                tip.setText("保存成功  保存路径已复制到剪切板");
                Clipboard clip = Toolkit.getDefaultToolkit().getSystemClipboard();
                Transferable tText = new StringSelection(saving.getDirectory());
                clip.setContents(tText, null);
            }
        } else {
            tip.setText("保存失败！请选择保存路径");
        }

    }

    //刷新面板
    private void refresh() {
        int x = component.getLocationOnScreen().x;
        int y = component.getLocationOnScreen().y;
        ArrayList<Character> comChar = comToArray();
        boolean FuDian = fuDian[0];
        boolean HadChar = hadChar[0];
        component.dispose();
        componentFrame componentFrame = new componentFrame();
        componentFrame.componentInitialize();
        componentFrame.component.setLocation(x, y);
        componentFrame.com.setText(arrayTo.arrayToString(comChar));
        componentFrame.hadChar[0] = HadChar;
        componentFrame.fuDian[0] = FuDian;
    }

    //dialog居中
    public void dialogCenter(JDialog j, int dialogX, int dialogY) {
        locationOnScreen = component.getLocationOnScreen();
        componentSize = component.getSize();
        j.setBounds(locationOnScreen.x + componentSize.width / 2 - dialogX / 2, locationOnScreen.y + componentSize.height / 2 - dialogY / 2, dialogX, dialogY);
        j.setVisible(true);
        j.setResizable(false);
    }

    //提示信息面板 用标题提示
    private void dialogTip(Frame owner, int sizeX, int sizeY, String tip) {
        Dialog dialog = new Dialog(owner, tip, true);
        dialog.setResizable(false);
        locationOnScreen = component.getLocationOnScreen();
        componentSize = component.getSize();
        dialog.setBounds(locationOnScreen.x + componentSize.width / 2 - sizeX / 2, locationOnScreen.y + componentSize.height / 2 - sizeY / 2, sizeX, sizeY);
        dialog.addWindowListener(new WindowAdapter() {
            @Override
            public void windowClosing(WindowEvent e) {
                dialog.dispose();
            }
        });
        dialog.setVisible(true);
    }
}
